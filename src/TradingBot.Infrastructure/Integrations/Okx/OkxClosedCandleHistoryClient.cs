using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxClosedCandleHistoryClient(
    HttpClient httpClient,
    TimeProvider timeProvider) : IClosedCandleHistoryClient
{
    private const int MaximumPageSize = 300;

    public async ValueTask<IReadOnlyList<Candle>> GetAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        EnsureConfiguration(instrumentId);
        var bar = MapBar(timeframe);
        if (!timeframe.IsBoundary(fromInclusive) ||
            !timeframe.IsBoundary(toExclusive) ||
            toExclusive <= fromInclusive)
        {
            throw new DomainRuleViolationException("OKX candle history range is invalid.");
        }

        var distance = toExclusive - fromInclusive;
        var count = distance.Ticks / timeframe.Duration.Ticks;
        if (distance.Ticks % timeframe.Duration.Ticks != 0 ||
            count is < 1 or > MaximumPageSize)
        {
            throw new DomainRuleViolationException("OKX candle history range exceeds one bounded page.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/v5/market/history-candles?instId={Uri.EscapeDataString(instrumentId.Symbol)}" +
            $"&bar={Uri.EscapeDataString(bar)}" +
            $"&after={toExclusive.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}" +
            $"&limit={count.ToString(CultureInfo.InvariantCulture)}");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OkxCandleResponse>(
            cancellationToken: cancellationToken)
            ?? throw new DomainRuleViolationException("OKX candle history response was empty.");
        if (!string.Equals(payload.Code, "0", StringComparison.Ordinal) ||
            payload.Data is null)
        {
            throw new DomainRuleViolationException(
                $"OKX candle history request failed with code {SanitizeCode(payload.Code)}.");
        }

        if (payload.Data.Length != count)
        {
            throw new DomainRuleViolationException("OKX candle history did not cover the requested range.");
        }

        var knownAt = timeProvider.GetUtcNow();
        var candles = new Candle[payload.Data.Length];
        for (var sourceIndex = 0; sourceIndex < payload.Data.Length; sourceIndex++)
        {
            var targetIndex = payload.Data.Length - sourceIndex - 1;
            candles[targetIndex] = ParseClosedCandle(
                payload.Data[sourceIndex],
                instrumentId,
                timeframe,
                knownAt);
        }

        var expectedOpenTime = fromInclusive;
        foreach (var candle in candles)
        {
            if (candle.OpenTime != expectedOpenTime)
            {
                throw new DomainRuleViolationException(
                    "OKX candle history was not contiguous within the requested range.");
            }

            expectedOpenTime = candle.CloseTime;
        }

        return Array.AsReadOnly(candles);
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

    private static string MapBar(Timeframe timeframe) => timeframe.Duration switch
    {
        var duration when duration == TimeSpan.FromSeconds(1) => "1s",
        var duration when duration == TimeSpan.FromMinutes(1) => "1m",
        var duration when duration == TimeSpan.FromMinutes(3) => "3m",
        var duration when duration == TimeSpan.FromMinutes(5) => "5m",
        var duration when duration == TimeSpan.FromMinutes(15) => "15m",
        var duration when duration == TimeSpan.FromMinutes(30) => "30m",
        var duration when duration == TimeSpan.FromHours(1) => "1H",
        var duration when duration == TimeSpan.FromHours(2) => "2H",
        var duration when duration == TimeSpan.FromHours(4) => "4H",
        var duration when duration == TimeSpan.FromHours(6) => "6Hutc",
        var duration when duration == TimeSpan.FromHours(12) => "12Hutc",
        var duration when duration == TimeSpan.FromDays(1) => "1Dutc",
        _ => throw new DomainRuleViolationException("Timeframe is not supported by the OKX UTC candle adapter.")
    };

    private static Candle ParseClosedCandle(
        string[] row,
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset knownAt)
    {
        if (row.Length < 9 ||
            !string.Equals(row[8], "1", StringComparison.Ordinal) ||
            !long.TryParse(row[0], NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) ||
            !TryPositiveDecimal(row[1], out var open) ||
            !TryPositiveDecimal(row[2], out var high) ||
            !TryPositiveDecimal(row[3], out var low) ||
            !TryPositiveDecimal(row[4], out var close) ||
            !TryNonNegativeDecimal(row[5], out var baseVolume))
        {
            throw new DomainRuleViolationException("OKX closed candle payload was invalid.");
        }

        try
        {
            return Candle.CreateClosed(
                instrumentId,
                timeframe,
                DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                knownAt,
                open,
                high,
                low,
                close,
                baseVolume);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException("OKX closed candle timestamp was invalid.");
        }
    }

    private static bool TryPositiveDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed > 0m;

    private static bool TryNonNegativeDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed >= 0m;

    private static string SanitizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 16 ? "unknown" : code;

    private sealed record OkxCandleResponse(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("data")] string[][]? Data);
}
