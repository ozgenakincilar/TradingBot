using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxBooks5MessageParser
{
    public PaperMarketEvent? Parse(
        string json,
        InstrumentId expectedInstrument,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var payload = JsonSerializer.Deserialize<OkxWebSocketMessage>(json)
            ?? throw new DomainRuleViolationException("OKX WebSocket payload was empty.");
        if (payload.Event is not null)
        {
            if (string.Equals(payload.Event, "error", StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    $"OKX WebSocket request failed with code {SanitizeCode(payload.Code)}.");
            }

            return null;
        }

        var argument = payload.Argument;
        if (!string.Equals(argument?.Channel, "books5", StringComparison.Ordinal) ||
            !string.Equals(argument?.InstrumentId, expectedInstrument.Symbol, StringComparison.Ordinal) ||
            payload.Data is not { Length: 1 })
        {
            throw new DomainRuleViolationException("Unexpected OKX books5 WebSocket payload.");
        }

        var book = payload.Data[0];
        if (book.Sequence < 0 || book.PreviousSequence < -1 ||
            !long.TryParse(book.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            throw new DomainRuleViolationException("OKX books5 data was invalid.");
        }

        var bids = OkxOrderBookDepthParser.Parse(book.Bids);
        var asks = OkxOrderBookDepthParser.Parse(book.Asks);

        var snapshot = new PaperTopOfBookSnapshot(
            expectedInstrument,
            bids[0].Price,
            bids[0].Quantity,
            asks[0].Price,
            asks[0].Quantity,
            DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
            bids,
            asks);
        snapshot.Validate();
        return new PaperMarketEvent(
            $"okx-ws-books5-{expectedInstrument.Symbol}-{book.Sequence}",
            book.Sequence,
            receivedAt,
            snapshot,
            book.PreviousSequence >= 0 ? book.PreviousSequence : null);
    }

    private static string SanitizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 16 ? "unknown" : code;

    private sealed record OkxWebSocketMessage(
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("arg")] OkxArgument? Argument,
        [property: JsonPropertyName("data")] OkxBook[]? Data);

    private sealed record OkxArgument(
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("instId")] string InstrumentId);

    private sealed record OkxBook(
        [property: JsonPropertyName("asks")] string[][]? Asks,
        [property: JsonPropertyName("bids")] string[][]? Bids,
        [property: JsonPropertyName("ts")] string Timestamp,
        [property: JsonPropertyName("prevSeqId")] long PreviousSequence,
        [property: JsonPropertyName("seqId")] long Sequence);
}
