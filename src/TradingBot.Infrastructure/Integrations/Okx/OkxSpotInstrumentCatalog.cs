using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxSpotInstrumentCatalog(HttpClient httpClient) : ISpotInstrumentCatalog
{
    public async ValueTask<SpotInstrumentMetadata> GetAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration(instrumentId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v5/public/instruments?instType=SPOT&instId={Uri.EscapeDataString(instrumentId.Symbol)}");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OkxInstrumentResponse>(
            cancellationToken: cancellationToken)
            ?? throw new DomainRuleViolationException("OKX instrument response was empty.");
        if (!string.Equals(payload.Code, "0", StringComparison.Ordinal) ||
            payload.Data is not { Length: 1 })
        {
            throw new DomainRuleViolationException(
                $"OKX instrument request failed with code {SanitizeCode(payload.Code)}.");
        }

        var item = payload.Data[0];
        if (!string.Equals(item.InstrumentType, "SPOT", StringComparison.Ordinal) ||
            !string.Equals(item.InstrumentId, instrumentId.Symbol, StringComparison.Ordinal) ||
            !string.Equals(
                $"{item.BaseCurrency}-{item.QuoteCurrency}",
                instrumentId.Symbol,
                StringComparison.Ordinal) ||
            !TryPositiveDecimal(item.TickSize, out var tickSize) ||
            !TryPositiveDecimal(item.LotSize, out var lotSize) ||
            !TryPositiveDecimal(item.MinimumSize, out var minimumSize))
        {
            throw new DomainRuleViolationException("OKX Spot instrument payload was invalid.");
        }

        return new SpotInstrumentMetadata(
            instrumentId,
            AssetCode.Create(item.BaseCurrency),
            AssetCode.Create(item.QuoteCurrency),
            tickSize,
            lotSize,
            minimumSize,
            string.Equals(item.State, "live", StringComparison.Ordinal),
            item.State);
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

    private sealed record OkxInstrumentResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("data")] OkxInstrument[]? Data);

    private sealed record OkxInstrument(
        [property: JsonPropertyName("instType")] string InstrumentType,
        [property: JsonPropertyName("instId")] string InstrumentId,
        [property: JsonPropertyName("baseCcy")] string BaseCurrency,
        [property: JsonPropertyName("quoteCcy")] string QuoteCurrency,
        [property: JsonPropertyName("tickSz")] string TickSize,
        [property: JsonPropertyName("lotSz")] string LotSize,
        [property: JsonPropertyName("minSz")] string MinimumSize,
        [property: JsonPropertyName("state")] string State);
}
