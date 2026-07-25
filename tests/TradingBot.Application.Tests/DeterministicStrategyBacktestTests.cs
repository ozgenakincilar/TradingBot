using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class DeterministicStrategyBacktestTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset End =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FutureTrendCandleCannotChangeCurrentDecision()
    {
        var signals = SignalSeries();
        var knownTrends = TrendSeries(includeFuture: false);
        var trendsWithFuture = TrendSeries(includeFuture: true);
        var backtest = new DeterministicStrategyBacktest();

        var withoutFuture = await RunAsync(backtest, signals, knownTrends);
        var withFuture = await RunAsync(backtest, signals, trendsWithFuture);

        var expected = Assert.Single(withoutFuture);
        var actual = Assert.Single(withFuture);
        Assert.Equal(expected, actual);
        Assert.Equal(StrategyAction.EnterLong, actual.Decision.Action);
        Assert.Equal(StrategyPositionState.Long, actual.PositionAfterDecision);
    }

    [Fact]
    public async Task GapInHistoricalSignalStreamFailsClosed()
    {
        var signals = SignalSeries().ToList();
        signals.RemoveAt(10);
        var backtest = new DeterministicStrategyBacktest();

        var action = () => RunAsync(backtest, signals, TrendSeries(includeFuture: false));

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private static async Task<List<StrategyBacktestDecision>> RunAsync(
        DeterministicStrategyBacktest backtest,
        IEnumerable<Candle> signals,
        IEnumerable<Candle> trends)
    {
        var results = new List<StrategyBacktestDecision>();
        await foreach (var result in backtest.RunAsync(
                           Definition(),
                           ToAsync(signals),
                           ToAsync(trends),
                           CancellationToken.None))
        {
            results.Add(result);
        }

        return results;
    }

    private static async IAsyncEnumerable<Candle> ToAsync(IEnumerable<Candle> candles)
    {
        foreach (var candle in candles)
        {
            yield return candle;
        }

        await Task.CompletedTask;
    }

    private static IReadOnlyList<Candle> SignalSeries()
    {
        var start = End - (Signal.Duration * 200);
        return Enumerable.Range(0, 200)
            .Select(index => Create(
                Signal,
                start + (Signal.Duration * index),
                index == 198 ? 99m : index == 199 ? 101m : 100m))
            .ToArray();
    }

    private static IReadOnlyList<Candle> TrendSeries(bool includeFuture)
    {
        var start = End - (Trend.Duration * 200);
        var candles = Enumerable.Range(0, 200)
            .Select(index => Create(
                Trend,
                start + (Trend.Duration * index),
                index == 199 ? 110m : 100m))
            .ToList();
        if (includeFuture)
        {
            candles.Add(Create(Trend, End, 1m));
        }

        return candles;
    }

    private static Candle Create(Timeframe timeframe, DateTimeOffset openTime, decimal close) =>
        Candle.CreateClosed(
            Instrument,
            timeframe,
            openTime,
            End.AddHours(3),
            100m,
            Math.Max(100m, close),
            Math.Min(100m, close),
            close,
            1m);

    private static StrategyDefinition Definition() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline",
        1,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 20,
        trendEmaPeriod: 200,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 200,
        minimumTrendWarmupCandles: 200);
}
