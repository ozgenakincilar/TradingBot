namespace TradingBot.Host;

public sealed record TradingReadinessSnapshot(
    bool InstrumentReady,
    bool MarketDataReady,
    bool CandleHistoryRequired,
    bool SignalCandleHistoryReady,
    bool TrendCandleHistoryReady,
    string? Instrument,
    int? SignalCandleTimeframeSeconds,
    int? SignalWarmupCandleCount,
    int? TrendCandleTimeframeSeconds,
    int? TrendWarmupCandleCount,
    string? Reason)
{
    public bool CandleHistoryReady =>
        SignalCandleHistoryReady && TrendCandleHistoryReady;

    public bool IsReady =>
        InstrumentReady &&
        MarketDataReady &&
        (!CandleHistoryRequired || CandleHistoryReady);
}

public sealed class TradingReadinessState
{
    private TradingReadinessSnapshot _snapshot;

    public TradingReadinessState(bool candleHistoryRequired = false)
    {
        _snapshot = new(
            false,
            false,
            candleHistoryRequired,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            "starting");
    }

    public TradingReadinessSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void MarkInstrumentReady(string instrument) =>
        Update(current => current with
        {
            InstrumentReady = true,
            Instrument = instrument,
            Reason = GetReason(
                true,
                current.MarketDataReady,
                current.CandleHistoryRequired,
                current.CandleHistoryReady)
        });

    public void MarkMarketDataReady() =>
        Update(current => current with
        {
            MarketDataReady = true,
            Reason = GetReason(
                current.InstrumentReady,
                true,
                current.CandleHistoryRequired,
                current.CandleHistoryReady)
        });

    public void MarkSignalCandleHistoryReady(int timeframeSeconds, int warmupCandleCount) =>
        Update(current => current with
        {
            SignalCandleHistoryReady = true,
            SignalCandleTimeframeSeconds = timeframeSeconds,
            SignalWarmupCandleCount = warmupCandleCount,
            Reason = GetReason(
                current.InstrumentReady,
                current.MarketDataReady,
                current.CandleHistoryRequired,
                current.TrendCandleHistoryReady)
        });

    public void MarkSignalCandleHistoryNotReady(string reason) =>
        Update(current => current with { SignalCandleHistoryReady = false, Reason = reason });

    public void MarkTrendCandleHistoryReady(int timeframeSeconds, int warmupCandleCount) =>
        Update(current => current with
        {
            TrendCandleHistoryReady = true,
            TrendCandleTimeframeSeconds = timeframeSeconds,
            TrendWarmupCandleCount = warmupCandleCount,
            Reason = GetReason(
                current.InstrumentReady,
                current.MarketDataReady,
                current.CandleHistoryRequired,
                current.SignalCandleHistoryReady)
        });

    public void MarkTrendCandleHistoryNotReady(string reason) =>
        Update(current => current with { TrendCandleHistoryReady = false, Reason = reason });

    public void MarkMarketDataNotReady(string reason) =>
        Update(current => current with { MarketDataReady = false, Reason = reason });

    public void MarkInstrumentNotReady(string reason) =>
        Update(current => current with { InstrumentReady = false, Reason = reason });

    private static string? GetReason(
        bool instrumentReady,
        bool marketDataReady,
        bool candleHistoryRequired,
        bool candleHistoryReady)
    {
        if (!instrumentReady)
        {
            return "instrument-not-ready";
        }

        if (candleHistoryRequired && !candleHistoryReady)
        {
            return "candle-history-not-ready";
        }

        return marketDataReady ? null : "market-data-not-ready";
    }

    private void Update(Func<TradingReadinessSnapshot, TradingReadinessSnapshot> update)
    {
        while (true)
        {
            var current = Snapshot;
            var next = update(current);
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref _snapshot, next, current),
                    current))
            {
                return;
            }
        }
    }
}
