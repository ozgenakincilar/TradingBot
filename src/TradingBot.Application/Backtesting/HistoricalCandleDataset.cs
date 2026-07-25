using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public sealed record HistoricalCandleDatasetDescriptor(
    string SourceId,
    string SchemaVersion,
    string Sha256,
    InstrumentId InstrumentId,
    Timeframe Timeframe);

public sealed record HistoricalCandleDatasetSummary(
    long CandleCount,
    DateTimeOffset FirstOpenTime,
    DateTimeOffset LastCloseTime);

public interface IHistoricalCandleDataset : IAsyncDisposable
{
    HistoricalCandleDatasetDescriptor Descriptor { get; }

    HistoricalCandleDatasetSummary? CompletedSummary { get; }

    IAsyncEnumerable<Candle> ReadAsync(CancellationToken cancellationToken);
}

public static class HistoricalCandleDatasetContract
{
    public const string CsvSchemaVersion = "closed-candle-csv-v1";

    public const string CsvHeader = "open_time_utc,open,high,low,close,base_volume";

    public static void ValidateDescriptor(HistoricalCandleDatasetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsValidSourceId(descriptor.SourceId) ||
            descriptor.SchemaVersion != CsvSchemaVersion ||
            !IsSha256(descriptor.Sha256) ||
            descriptor.InstrumentId == default || descriptor.Timeframe == default)
        {
            throw new DomainRuleViolationException("Historical candle dataset descriptor is invalid.");
        }
    }

    private static bool IsValidSourceId(string? value) =>
        value is { Length: >= 3 and <= 128 } &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');
}
