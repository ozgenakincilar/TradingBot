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
            book.Bids is not { Length: > 0 } || book.Asks is not { Length: > 0 } ||
            book.Bids[0].Length < 2 || book.Asks[0].Length < 2 ||
            !TryPositiveDecimal(book.Bids[0][0], out var bidPrice) ||
            !TryPositiveDecimal(book.Bids[0][1], out var bidQuantity) ||
            !TryPositiveDecimal(book.Asks[0][0], out var askPrice) ||
            !TryPositiveDecimal(book.Asks[0][1], out var askQuantity) ||
            !long.TryParse(book.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            throw new DomainRuleViolationException("OKX books5 data was invalid.");
        }

        var snapshot = new PaperTopOfBookSnapshot(
            expectedInstrument,
            Price.From(bidPrice),
            bidQuantity,
            Price.From(askPrice),
            askQuantity,
            DateTimeOffset.FromUnixTimeMilliseconds(timestamp));
        snapshot.Validate();
        return new PaperMarketEvent(
            $"okx-ws-books5-{expectedInstrument.Symbol}-{book.Sequence}",
            book.Sequence,
            receivedAt,
            snapshot,
            book.PreviousSequence >= 0 ? book.PreviousSequence : null);
    }

    private static bool TryPositiveDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed > 0m;

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
