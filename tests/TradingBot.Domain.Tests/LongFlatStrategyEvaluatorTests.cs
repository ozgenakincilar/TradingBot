using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Domain.Tests;

public sealed class LongFlatStrategyEvaluatorTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset End =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BullishSignalCrossAndTrendEnterLong()
    {
        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(),
            SignalCandles(latestOpen: 100m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Flat);

        Assert.Equal(StrategyAction.EnterLong, decision.Action);
        Assert.Equal("signal-ema-cross-up", decision.ReasonCode);
        Assert.Equal(End, decision.EvaluatedAt);
    }

    [Fact]
    public void OversizedBullishCandleIsBlockedByFomoGuard()
    {
        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(),
            SignalCandles(latestOpen: 98m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Flat);

        Assert.Equal(StrategyAction.Hold, decision.Action);
        Assert.Equal("fomo-guard-blocked", decision.ReasonCode);
    }

    [Fact]
    public void LosingMacroTrendExitsLongBeforeSignalRule()
    {
        var trend = Series(Trend, 200, _ => 100m);
        trend[^1] = Create(Trend, 199, 90m, 90m, End - (Trend.Duration * 200));

        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(),
            Series(Signal, 200, _ => 100m),
            trend,
            StrategyPositionState.Long);

        Assert.Equal(StrategyAction.ExitToFlat, decision.Action);
        Assert.Equal("trend-filter-exit", decision.ReasonCode);
    }

    [Fact]
    public void SignalCrossDownExitsLongWhileMacroTrendRemainsBullish()
    {
        var closes = Enumerable.Repeat(100m, 200).ToArray();
        closes[^2] = 101m;
        closes[^1] = 99m;

        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(),
            Series(Signal, 200, index => closes[index]),
            BullishTrendCandles(),
            StrategyPositionState.Long);

        Assert.Equal(StrategyAction.ExitToFlat, decision.Action);
        Assert.Equal("signal-ema-cross-down", decision.ReasonCode);
    }

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

    private static IReadOnlyList<Candle> SignalCandles(decimal latestOpen, decimal latestClose)
    {
        var candles = Series(Signal, 200, index => index == 198 ? 99m : index == 199 ? latestClose : 100m);
        candles[^1] = Create(Signal, 199, latestOpen, latestClose, End - (Signal.Duration * 200));
        return candles;
    }

    private static IReadOnlyList<Candle> BullishTrendCandles()
    {
        var candles = Series(Trend, 200, _ => 100m);
        candles[^1] = Create(Trend, 199, 100m, 110m, End - (Trend.Duration * 200));
        return candles;
    }

    private static Candle[] Series(Timeframe timeframe, int count, Func<int, decimal> closeSelector)
    {
        var start = End - (timeframe.Duration * count);
        return Enumerable.Range(0, count)
            .Select(index => Create(timeframe, index, closeSelector(index), closeSelector(index), start))
            .ToArray();
    }

    private static Candle Create(
        Timeframe timeframe,
        int index,
        decimal open,
        decimal close,
        DateTimeOffset start) =>
        Candle.CreateClosed(
            Instrument,
            timeframe,
            start + (timeframe.Duration * index),
            End.AddHours(2),
            open,
            Math.Max(open, close),
            Math.Min(open, close),
            close,
            1m);
}
