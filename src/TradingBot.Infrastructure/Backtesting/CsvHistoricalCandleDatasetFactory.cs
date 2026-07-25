using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Backtesting;

public sealed record CsvHistoricalCandleDatasetRegistration(
    InstrumentId InstrumentId,
    Timeframe Timeframe,
    string FilePath,
    string SourceId,
    DateTimeOffset KnownAt);

public sealed class CsvHistoricalCandleDatasetFactory
    : IHistoricalCandleDatasetFactory
{
    private readonly IReadOnlyDictionary<(InstrumentId, Timeframe),
        CsvHistoricalCandleDatasetRegistration> _registrations;

    public CsvHistoricalCandleDatasetFactory(
        IEnumerable<CsvHistoricalCandleDatasetRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var materialized = registrations.ToArray();
        if (materialized.Length == 0 || materialized.Any(static registration =>
                registration.InstrumentId == default || registration.Timeframe == default ||
                string.IsNullOrWhiteSpace(registration.FilePath) ||
                registration.KnownAt == default || registration.KnownAt.Offset != TimeSpan.Zero))
        {
            throw new DomainRuleViolationException(
                "Historical CSV dataset registrations are invalid.");
        }

        var map = new Dictionary<(InstrumentId, Timeframe), CsvHistoricalCandleDatasetRegistration>();
        foreach (var registration in materialized)
        {
            HistoricalCandleDatasetContract.ValidateDescriptor(new HistoricalCandleDatasetDescriptor(
                registration.SourceId,
                HistoricalCandleDatasetContract.CsvSchemaVersion,
                new string('0', 64),
                registration.InstrumentId,
                registration.Timeframe));
            if (!map.TryAdd((registration.InstrumentId, registration.Timeframe), registration))
            {
                throw new DomainRuleViolationException(
                    "Historical CSV dataset registration identity must be unique.");
            }
        }

        _registrations = map;
    }

    public async ValueTask<IHistoricalCandleDataset> OpenAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        if (!_registrations.TryGetValue((instrumentId, timeframe), out var registration))
        {
            throw new DomainRuleViolationException(
                "Historical CSV dataset is not registered for the requested identity.");
        }

        return await CsvHistoricalCandleDataset.OpenAsync(
            registration.FilePath,
            registration.SourceId,
            instrumentId,
            timeframe,
            registration.KnownAt,
            cancellationToken);
    }
}
