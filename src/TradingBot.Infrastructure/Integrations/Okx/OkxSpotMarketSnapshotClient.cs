using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxSpotMarketSnapshotClient(
    HttpClient httpClient,
    TimeProvider timeProvider) : IMarketDataSnapshotClient
{
    public async ValueTask<PaperMarketEvent> GetRecoverySnapshotAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration(instrumentId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v5/market/books?instId={Uri.EscapeDataString(instrumentId.Symbol)}&sz=1");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OkxOrderBookResponse>(
            cancellationToken: cancellationToken)
            ?? throw new DomainRuleViolationException("OKX order book response was empty.");
        if (!string.Equals(payload.Code, "0", StringComparison.Ordinal) ||
            payload.Data is not { Length: 1 })
        {
            throw new DomainRuleViolationException(
                $"OKX order book request failed with code {SanitizeCode(payload.Code)}.");
        }

        var book = payload.Data[0];
        if (book.Sequence <= 0 ||
            book.Bids is not { Length: > 0 } ||
            book.Asks is not { Length: > 0 } ||
            book.Bids[0].Length < 2 ||
            book.Asks[0].Length < 2 ||
            !TryPositiveDecimal(book.Bids[0][0], out var bidPrice) ||
            !TryPositiveDecimal(book.Bids[0][1], out var bidQuantity) ||
            !TryPositiveDecimal(book.Asks[0][0], out var askPrice) ||
            !TryPositiveDecimal(book.Asks[0][1], out var askQuantity) ||
            !long.TryParse(book.Timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            throw new DomainRuleViolationException("OKX order book payload was invalid.");
        }

        var occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        return new PaperMarketEvent(
            $"okx-rest-{instrumentId.Symbol}-{book.Sequence}",
            book.Sequence,
            timeProvider.GetUtcNow(),
            new PaperTopOfBookSnapshot(
                instrumentId,
                Price.From(bidPrice),
                bidQuantity,
                Price.From(askPrice),
                askQuantity,
                occurredAt));
    }

    private void EnsureConfiguration(InstrumentId instrumentId)
    {
        if (httpClient.BaseAddress is null ||
            !string.Equals(httpClient.BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OKX REST base address must use HTTPS.");
        }

        if (!string.Equals(instrumentId.Exchange, "OKX", StringComparison.Ordinal) ||
            !instrumentId.Symbol.Contains('-', StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("OKX Spot instrument must use the BASE-QUOTE symbol format.");
        }
    }

    private static bool TryPositiveDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed > 0m;

    private static string SanitizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 16 ? "unknown" : code;

    private sealed record OkxOrderBookResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("data")] OkxOrderBook[]? Data);

    private sealed record OkxOrderBook(
        [property: JsonPropertyName("asks")] string[][]? Asks,
        [property: JsonPropertyName("bids")] string[][]? Bids,
        [property: JsonPropertyName("ts")] string Timestamp,
        [property: JsonPropertyName("seqId")] long Sequence);
}
