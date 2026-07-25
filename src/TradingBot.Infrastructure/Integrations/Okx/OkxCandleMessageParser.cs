using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Integrations.Okx;

public sealed class OkxCandleMessageParser
{
    public Candle? Parse(
        string json,
        InstrumentId expectedInstrument,
        IReadOnlyCollection<Timeframe> expectedTimeframes,
        DateTimeOffset receivedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(expectedTimeframes);
        var payload = JsonSerializer.Deserialize<OkxWebSocketMessage>(json)
            ?? throw new DomainRuleViolationException("OKX candle WebSocket payload was empty.");
        if (payload.Event is not null)
        {
            if (string.Equals(payload.Event, "error", StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    $"OKX candle WebSocket request failed with code {SanitizeCode(payload.Code)}.");
            }

            return null;
        }

        var argument = payload.Argument;
        if (!string.Equals(argument?.InstrumentId, expectedInstrument.Symbol, StringComparison.Ordinal) ||
            !TryMapTimeframe(argument?.Channel, out var timeframe) ||
            !expectedTimeframes.Contains(timeframe) ||
            payload.Data is not { Length: 1 } ||
            payload.Data[0] is not { Length: >= 9 } row)
        {
            throw new DomainRuleViolationException("Unexpected OKX candle WebSocket payload.");
        }

        if (string.Equals(row[8], "0", StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.Equals(row[8], "1", StringComparison.Ordinal) ||
            !long.TryParse(row[0], NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) ||
            !TryPositiveDecimal(row[1], out var open) ||
            !TryPositiveDecimal(row[2], out var high) ||
            !TryPositiveDecimal(row[3], out var low) ||
            !TryPositiveDecimal(row[4], out var close) ||
            !TryNonNegativeDecimal(row[5], out var baseVolume))
        {
            throw new DomainRuleViolationException("OKX closed candle WebSocket data was invalid.");
        }

        try
        {
            return Candle.CreateClosed(
                expectedInstrument,
                timeframe,
                DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                receivedAt,
                open,
                high,
                low,
                close,
                baseVolume);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "OKX candle WebSocket timestamp was invalid.");
        }
    }

    public static string MapChannel(Timeframe timeframe) => timeframe.Duration switch
    {
        var duration when duration == TimeSpan.FromMinutes(15) => "candle15m",
        var duration when duration == TimeSpan.FromHours(1) => "candle1H",
        _ => throw new DomainRuleViolationException(
            "Timeframe is not supported by the OKX strategy candle stream.")
    };

    private static bool TryMapTimeframe(string? channel, out Timeframe timeframe)
    {
        if (string.Equals(channel, "candle15m", StringComparison.Ordinal))
        {
            timeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
            return true;
        }

        if (string.Equals(channel, "candle1H", StringComparison.Ordinal))
        {
            timeframe = Timeframe.Create(TimeSpan.FromHours(1));
            return true;
        }

        timeframe = default;
        return false;
    }

    private static bool TryPositiveDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed > 0m;

    private static bool TryNonNegativeDecimal(string value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) && parsed >= 0m;

    private static string SanitizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) || code.Length > 16 ? "unknown" : code;

    private sealed record OkxWebSocketMessage(
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("arg")] OkxArgument? Argument,
        [property: JsonPropertyName("data")] string[][]? Data);

    private sealed record OkxArgument(
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("instId")] string InstrumentId);
}
