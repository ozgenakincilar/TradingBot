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

    [Fact]
    public void V2HysteresisBlocksCrossInsideCostBand()
    {
        var closes = Enumerable.Repeat(100m, 200).ToArray();
        closes[^2] = 99m;
        closes[^1] = 100.1m;

        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(version: 2, hysteresisBasisPoints: 30m),
            Series(Signal, 200, index => closes[index]),
            BullishTrendCandles(),
            StrategyPositionState.Flat);

        Assert.Equal(StrategyAction.Hold, decision.Action);
        Assert.Equal("no-entry-signal", decision.ReasonCode);
    }

    [Fact]
    public void V2HysteresisEntersOnlyAfterUpperBandCross()
    {
        var decision = LongFlatStrategyEvaluator.Evaluate(
            Definition(version: 2, hysteresisBasisPoints: 30m),
            SignalCandles(latestOpen: 100m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Flat);

        Assert.Equal(StrategyAction.EnterLong, decision.Action);
        Assert.Equal("signal-ema-hysteresis-cross-up", decision.ReasonCode);
    }

    [Fact]
    public void V2HysteresisHoldsInsideLowerBandAndExitsAfterCross()
    {
        var inside = Enumerable.Repeat(100m, 200).ToArray();
        inside[^2] = 101m;
        inside[^1] = 99.9m;
        var below = inside.ToArray();
        below[^1] = 99m;
        var definition = Definition(version: 2, hysteresisBasisPoints: 30m);

        var held = LongFlatStrategyEvaluator.Evaluate(
            definition,
            Series(Signal, 200, index => inside[index]),
            BullishTrendCandles(),
            StrategyPositionState.Long);
        var exited = LongFlatStrategyEvaluator.Evaluate(
            definition,
            Series(Signal, 200, index => below[index]),
            BullishTrendCandles(),
            StrategyPositionState.Long);

        Assert.Equal(StrategyAction.Hold, held.Action);
        Assert.Equal(StrategyAction.ExitToFlat, exited.Action);
        Assert.Equal("signal-ema-hysteresis-cross-down", exited.ReasonCode);
    }

    [Fact]
    public void V3ProfitProtectionExitsAfterActivatedPeakGivesBackFiftyBasisPoints()
    {
        var decision = LongFlatStrategyEvaluator.Evaluate(
            V3Definition(),
            SignalCandles(latestOpen: 101.5m, latestClose: 101.4m),
            BullishTrendCandles(),
            StrategyPositionState.Long,
            StrategyTradeContext.Open(100m).ObserveLongClose(102m));

        Assert.Equal(StrategyAction.ExitToFlat, decision.Action);
        Assert.Equal("profit-protection-exit", decision.ReasonCode);
    }

    [Fact]
    public void V3ProfitProtectionDoesNotExitBeforeActivation()
    {
        var decision = LongFlatStrategyEvaluator.Evaluate(
            V3Definition(),
            SignalCandles(latestOpen: 100.5m, latestClose: 100.4m),
            BullishTrendCandles(),
            StrategyPositionState.Long,
            StrategyTradeContext.Open(100m).ObserveLongClose(100.9m));

        Assert.Equal(StrategyAction.Hold, decision.Action);
        Assert.Equal("long-position-held", decision.ReasonCode);
    }

    [Fact]
    public void V3CooldownBlocksFourCompletedCandlesThenAllowsEntry()
    {
        var context = StrategyTradeContext.Closed();
        var blocked = LongFlatStrategyEvaluator.Evaluate(
            V3Definition(),
            SignalCandles(latestOpen: 100m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Flat,
            context);
        for (var index = 0; index < 4; index++)
        {
            context = context.AdvanceFlatCandle(4);
        }

        var allowed = LongFlatStrategyEvaluator.Evaluate(
            V3Definition(),
            SignalCandles(latestOpen: 100m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Flat,
            context);

        Assert.Equal(StrategyAction.Hold, blocked.Action);
        Assert.Equal("reentry-cooldown-blocked", blocked.ReasonCode);
        Assert.Equal(StrategyAction.EnterLong, allowed.Action);
    }

    [Fact]
    public void V3LongPositionRequiresMatchingTradeContext()
    {
        var action = () => LongFlatStrategyEvaluator.Evaluate(
            V3Definition(),
            SignalCandles(latestOpen: 100m, latestClose: 101m),
            BullishTrendCandles(),
            StrategyPositionState.Long);

        Assert.Throws<TradingBot.Domain.Common.DomainRuleViolationException>(action);
    }

    private static StrategyDefinition Definition(
        int version = 1,
        decimal hysteresisBasisPoints = 0m) => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline",
        version,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 20,
        trendEmaPeriod: 200,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 200,
        minimumTrendWarmupCandles: 200,
        signalEmaHysteresisBasisPoints: hysteresisBasisPoints);

    private static StrategyDefinition V3Definition() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline",
        3,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 20,
        trendEmaPeriod: 200,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 200,
        minimumTrendWarmupCandles: 200,
        signalEmaHysteresisBasisPoints: 30m,
        reentryCooldownCandles: 4,
        profitProtectionActivationBasisPoints: 100m,
        profitProtectionTrailingBasisPoints: 50m);

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
