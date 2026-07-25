using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class ClosedCandleSeriesStoreTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly DateTimeOffset Start =
        new(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAndAppendRetainOnlyBoundedLatestSeries()
    {
        var store = new ClosedCandleSeriesStore(capacityPerSeries: 3);
        await store.SeedAsync(Warmup(0, 3), CancellationToken.None);

        var status = await store.AppendAsync(CandleAt(3), CancellationToken.None);
        var snapshot = await store.GetSnapshotAsync(Instrument, Timeframe, CancellationToken.None);

        Assert.Equal(ClosedCandleSeriesUpdateStatus.Appended, status);
        Assert.True(snapshot.IsReady);
        Assert.Equal([Start + Timeframe.Duration, Start + (Timeframe.Duration * 2), Start + (Timeframe.Duration * 3)],
            snapshot.Candles.Select(static candle => candle.OpenTime));
    }

    [Fact]
    public async Task DuplicateAndOlderUpdatesDoNotMutateReadySeries()
    {
        var store = new ClosedCandleSeriesStore(capacityPerSeries: 3);
        await store.SeedAsync(Warmup(0, 2), CancellationToken.None);

        var duplicate = await store.AppendAsync(CandleAt(1), CancellationToken.None);
        var older = await store.AppendAsync(CandleAt(0), CancellationToken.None);
        var snapshot = await store.GetSnapshotAsync(Instrument, Timeframe, CancellationToken.None);

        Assert.Equal(ClosedCandleSeriesUpdateStatus.Duplicate, duplicate);
        Assert.Equal(ClosedCandleSeriesUpdateStatus.OutOfOrder, older);
        Assert.True(snapshot.IsReady);
        Assert.Equal(2, snapshot.Candles.Count);
    }

    [Fact]
    public async Task ExplicitClosePreservesEvidenceButRejectsLiveUpdatesUntilReseed()
    {
        var store = new ClosedCandleSeriesStore(capacityPerSeries: 3);
        await store.SeedAsync(Warmup(0, 2), CancellationToken.None);

        await store.MarkNotReadyAsync(Instrument, Timeframe, CancellationToken.None);
        var snapshot = await store.GetSnapshotAsync(Instrument, Timeframe, CancellationToken.None);

        Assert.False(snapshot.IsReady);
        Assert.Equal(2, snapshot.Candles.Count);
        await Assert.ThrowsAsync<DomainRuleViolationException>(async () =>
            await store.AppendAsync(CandleAt(2), CancellationToken.None));
    }

    [Theory]
    [InlineData(3, ClosedCandleSeriesUpdateStatus.GapDetected)]
    [InlineData(1, ClosedCandleSeriesUpdateStatus.Conflicting)]
    public async Task GapOrConflictFailClosed(
        int updateIndex,
        ClosedCandleSeriesUpdateStatus expected)
    {
        var store = new ClosedCandleSeriesStore(capacityPerSeries: 3);
        await store.SeedAsync(Warmup(0, 2), CancellationToken.None);
        var update = updateIndex == 1 ? CandleAt(1, close: 101m) : CandleAt(updateIndex);

        var status = await store.AppendAsync(update, CancellationToken.None);
        var snapshot = await store.GetSnapshotAsync(Instrument, Timeframe, CancellationToken.None);

        Assert.Equal(expected, status);
        Assert.False(snapshot.IsReady);
        await Assert.ThrowsAsync<DomainRuleViolationException>(async () =>
            await store.AppendAsync(CandleAt(2), CancellationToken.None));
    }

    private static ClosedCandleWarmupResult Warmup(int startIndex, int count)
    {
        var candles = Enumerable.Range(startIndex, count)
            .Select(static index => CandleAt(index))
            .ToArray();
        return new ClosedCandleWarmupResult(
            Instrument,
            Timeframe,
            candles[0].OpenTime,
            candles[^1].CloseTime,
            candles);
    }

    private static Candle CandleAt(int index, decimal close = 100m) =>
        Candle.CreateClosed(
            Instrument,
            Timeframe,
            Start + (Timeframe.Duration * index),
            Start + (Timeframe.Duration * 10),
            100m,
            102m,
            99m,
            close,
            1m);
}
