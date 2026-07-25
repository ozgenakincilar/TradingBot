namespace TradingBot.Host;

public sealed record TradingReadinessSnapshot(
    bool InstrumentReady,
    bool MarketDataReady,
    string? Instrument,
    string? Reason)
{
    public bool IsReady => InstrumentReady && MarketDataReady;
}

public sealed class TradingReadinessState
{
    private TradingReadinessSnapshot _snapshot = new(false, false, null, "starting");

    public TradingReadinessSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void MarkInstrumentReady(string instrument) =>
        Update(current => current with
        {
            InstrumentReady = true,
            Instrument = instrument,
            Reason = current.MarketDataReady ? null : "market-data-not-ready"
        });

    public void MarkMarketDataReady() =>
        Update(current => current with
        {
            MarketDataReady = true,
            Reason = current.InstrumentReady ? null : "instrument-not-ready"
        });

    public void MarkMarketDataNotReady(string reason) =>
        Update(current => current with { MarketDataReady = false, Reason = reason });

    public void MarkInstrumentNotReady(string reason) =>
        Update(current => current with { InstrumentReady = false, Reason = reason });

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
