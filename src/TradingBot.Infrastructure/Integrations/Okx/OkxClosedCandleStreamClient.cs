using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxClosedCandleStreamClient(
    Uri endpoint,
    TimeProvider timeProvider,
    OkxCandleMessageParser parser) : IClosedCandleStreamClient
{
    private const int ReceiveBufferSize = 8 * 1024;
    private const int MaximumMessageSize = 64 * 1024;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public async IAsyncEnumerable<Candle> ReadClosedAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestedTimeframes = ValidateAndCopy(endpoint, instrumentId, timeframes);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        await socket.ConnectAsync(endpoint, cancellationToken);
        await SendTextAsync(
            socket,
            Subscription(instrumentId.Symbol, requestedTimeframes),
            cancellationToken);

        var awaitingPong = false;
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            string message;
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            heartbeat.CancelAfter(HeartbeatInterval);
            try
            {
                message = await ReceiveTextAsync(socket, heartbeat.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (awaitingPong)
                {
                    throw new IOException("OKX candle WebSocket heartbeat timed out.");
                }

                await SendTextAsync(socket, "ping", cancellationToken);
                awaitingPong = true;
                continue;
            }

            if (string.Equals(message, "pong", StringComparison.Ordinal))
            {
                awaitingPong = false;
                continue;
            }

            awaitingPong = false;
            var candle = parser.Parse(
                message,
                instrumentId,
                requestedTimeframes,
                timeProvider.GetUtcNow());
            if (candle is not null)
            {
                yield return candle;
            }
        }
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            var writer = new ArrayBufferWriter<byte>(ReceiveBufferSize);
            while (true)
            {
                var result = await socket.ReceiveAsync(rented.AsMemory(), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new IOException("OKX candle WebSocket closed the connection.");
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new DomainRuleViolationException(
                        "OKX candle WebSocket sent a non-text message.");
                }

                if (writer.WrittenCount + result.Count > MaximumMessageSize)
                {
                    throw new DomainRuleViolationException(
                        "OKX candle WebSocket message exceeded the size limit.");
                }

                writer.Write(rented.AsSpan(0, result.Count));
                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(writer.WrittenSpan);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private static Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken cancellationToken) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(message),
            WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage,
            cancellationToken).AsTask();

    private static string Subscription(
        string symbol,
        IReadOnlyCollection<Timeframe> timeframes) =>
        JsonSerializer.Serialize(new SubscriptionRequest(
            "subscribe",
            timeframes.Select(timeframe =>
                new SubscriptionArgument(
                    OkxCandleMessageParser.MapChannel(timeframe),
                    symbol)).ToArray()));

    private static Timeframe[] ValidateAndCopy(
        Uri webSocketEndpoint,
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes)
    {
        ArgumentNullException.ThrowIfNull(timeframes);
        if (!webSocketEndpoint.IsAbsoluteUri ||
            !string.Equals(webSocketEndpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(webSocketEndpoint.AbsolutePath, "/ws/v5/business", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OKX candle WebSocket endpoint must use the WSS business path.");
        }

        if (!string.Equals(instrumentId.Exchange, "OKX", StringComparison.Ordinal) ||
            !instrumentId.Symbol.Contains('-', StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "OKX Spot instrument must use the BASE-QUOTE symbol format.");
        }

        var copy = timeframes.ToArray();
        if (copy.Length is < 1 or > 8 ||
            copy.Any(static timeframe => timeframe == default) ||
            copy.Distinct().Count() != copy.Length)
        {
            throw new DomainRuleViolationException(
                "OKX candle stream timeframes must be bounded, valid, and unique.");
        }

        foreach (var timeframe in copy)
        {
            _ = OkxCandleMessageParser.MapChannel(timeframe);
        }

        return copy;
    }

    private sealed record SubscriptionRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("op")] string Operation,
        [property: System.Text.Json.Serialization.JsonPropertyName("args")] SubscriptionArgument[] Arguments);

    private sealed record SubscriptionArgument(
        [property: System.Text.Json.Serialization.JsonPropertyName("channel")] string Channel,
        [property: System.Text.Json.Serialization.JsonPropertyName("instId")] string InstrumentId);
}
