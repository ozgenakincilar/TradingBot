using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxSpotMarketStreamClient(
    Uri endpoint,
    TimeProvider timeProvider,
    OkxBooks5MessageParser parser) : IMarketDataStreamClient
{
    private const int ReceiveBufferSize = 8 * 1024;
    private const int MaximumMessageSize = 64 * 1024;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public async IAsyncEnumerable<PaperMarketEvent> ReadTopOfBookAsync(
        InstrumentId instrumentId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Validate(endpoint, instrumentId);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        await socket.ConnectAsync(endpoint, cancellationToken);
        await SendTextAsync(socket, Subscription(instrumentId.Symbol), cancellationToken);

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
                    throw new IOException("OKX WebSocket heartbeat timed out.");
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
            var marketEvent = parser.Parse(message, instrumentId, timeProvider.GetUtcNow());
            if (marketEvent is not null)
            {
                yield return marketEvent;
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
                    throw new IOException("OKX WebSocket closed the connection.");
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new DomainRuleViolationException("OKX WebSocket sent a non-text message.");
                }

                if (writer.WrittenCount + result.Count > MaximumMessageSize)
                {
                    throw new DomainRuleViolationException("OKX WebSocket message exceeded the size limit.");
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

    private static string Subscription(string symbol) =>
        $$"""{"op":"subscribe","args":[{"channel":"books5","instId":"{{symbol}}"}]}""";

    private static void Validate(Uri webSocketEndpoint, InstrumentId instrumentId)
    {
        if (!webSocketEndpoint.IsAbsoluteUri ||
            !string.Equals(webSocketEndpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OKX WebSocket endpoint must use WSS.");
        }

        if (!string.Equals(instrumentId.Exchange, "OKX", StringComparison.Ordinal) ||
            !instrumentId.Symbol.Contains('-', StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("OKX Spot instrument must use the BASE-QUOTE symbol format.");
        }
    }
}
