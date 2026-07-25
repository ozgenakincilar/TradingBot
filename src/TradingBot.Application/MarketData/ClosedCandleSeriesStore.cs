using System.Collections.ObjectModel;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public enum ClosedCandleSeriesUpdateStatus
{
    Seeded = 1,
    Appended = 2,
    Duplicate = 3,
    OutOfOrder = 4,
    GapDetected = 5,
    Conflicting = 6
}

public sealed record ClosedCandleSeriesSnapshot(
    InstrumentId InstrumentId,
    Timeframe Timeframe,
    bool IsReady,
    IReadOnlyList<Candle> Candles);

public sealed class ClosedCandleSeriesStore
{
    private readonly int _capacityPerSeries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SeriesKey, SeriesState> _series = [];

    public ClosedCandleSeriesStore(int capacityPerSeries)
    {
        if (capacityPerSeries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityPerSeries));
        }

        _capacityPerSeries = capacityPerSeries;
    }

    public async ValueTask<ClosedCandleSeriesUpdateStatus> SeedAsync(
        ClosedCandleWarmupResult warmup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warmup);
        ValidateSeed(warmup);
        var retained = warmup.Candles.TakeLast(_capacityPerSeries).ToArray();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _series[new SeriesKey(warmup.InstrumentId, warmup.Timeframe)] =
                new SeriesState(retained, isReady: true);
            return ClosedCandleSeriesUpdateStatus.Seeded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ClosedCandleSeriesUpdateStatus> AppendAsync(
        Candle candle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candle);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var key = new SeriesKey(candle.InstrumentId, candle.Timeframe);
            if (!_series.TryGetValue(key, out var state) || !state.IsReady || state.Candles.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "Closed-candle series must be seeded and ready before live updates.");
            }

            var latest = state.Candles[^1];
            if (candle.OpenTime < latest.OpenTime)
            {
                return ClosedCandleSeriesUpdateStatus.OutOfOrder;
            }

            if (candle.OpenTime == latest.OpenTime)
            {
                if (candle == latest)
                {
                    return ClosedCandleSeriesUpdateStatus.Duplicate;
                }

                state.IsReady = false;
                return ClosedCandleSeriesUpdateStatus.Conflicting;
            }

            if (candle.OpenTime != latest.CloseTime)
            {
                state.IsReady = false;
                return ClosedCandleSeriesUpdateStatus.GapDetected;
            }

            state.Candles.Add(candle);
            if (state.Candles.Count > _capacityPerSeries)
            {
                state.Candles.RemoveAt(0);
            }

            return ClosedCandleSeriesUpdateStatus.Appended;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ClosedCandleSeriesSnapshot> GetSnapshotAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_series.TryGetValue(new SeriesKey(instrumentId, timeframe), out var state))
            {
                return new ClosedCandleSeriesSnapshot(
                    instrumentId,
                    timeframe,
                    false,
                    Array.Empty<Candle>());
            }

            return new ClosedCandleSeriesSnapshot(
                instrumentId,
                timeframe,
                state.IsReady,
                new ReadOnlyCollection<Candle>(state.Candles.ToArray()));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask MarkNotReadyAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_series.TryGetValue(new SeriesKey(instrumentId, timeframe), out var state))
            {
                state.IsReady = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateSeed(ClosedCandleWarmupResult warmup)
    {
        if (warmup.InstrumentId == default || warmup.Timeframe == default || warmup.Candles.Count == 0 ||
            warmup.Candles[0].OpenTime != warmup.FromInclusive ||
            warmup.Candles[^1].CloseTime != warmup.ToExclusive)
        {
            throw new DomainRuleViolationException("Closed-candle seed boundaries are incomplete.");
        }

        var guard = new ClosedCandleSequenceGuard(warmup.InstrumentId, warmup.Timeframe);
        var integrity = guard.ApplyRecovery(warmup.Candles);
        if (!integrity.IsReady || integrity.Status != ClosedCandleIntegrityStatus.RecoveryApplied)
        {
            throw new DomainRuleViolationException("Closed-candle seed is not a contiguous series.");
        }
    }

    private readonly record struct SeriesKey(InstrumentId InstrumentId, Timeframe Timeframe);

    private sealed class SeriesState(IEnumerable<Candle> candles, bool isReady)
    {
        public List<Candle> Candles { get; } = [.. candles];

        public bool IsReady { get; set; } = isReady;
    }
}
