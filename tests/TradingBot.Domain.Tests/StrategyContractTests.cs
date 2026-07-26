using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Domain.Tests;

public sealed class StrategyContractTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe SignalTimeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe TrendTimeframe = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset EvaluationTime =
        new(2026, 7, 25, 12, 15, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovedBaselineIsVersionedLongFlatAndWarmupBounded()
    {
        var definition = Definition();

        Assert.Equal("btc-usdt-long-flat-baseline", definition.StrategyId);
        Assert.Equal(1, definition.Version);
        Assert.Equal(StrategyExposurePolicy.LongFlat, definition.ExposurePolicy);
        Assert.Equal(20, definition.SignalEmaPeriod);
        Assert.Equal(200, definition.TrendEmaPeriod);
        Assert.Equal(2m, definition.MaximumSignalCandleMovePercent);
        Assert.Equal(200, definition.MinimumSignalWarmupCandles);
        Assert.Equal(200, definition.MinimumTrendWarmupCandles);
        Assert.Equal(0m, definition.SignalEmaHysteresisBasisPoints);
    }

    [Fact]
    public void CostAwareV2CarriesBoundedHysteresis()
    {
        var definition = StrategyDefinition.Create(
            "btc-usdt-long-flat-baseline",
            2,
            Instrument,
            SignalTimeframe,
            TrendTimeframe,
            20,
            200,
            2m,
            200,
            200,
            signalEmaHysteresisBasisPoints: 30m);

        Assert.Equal(2, definition.Version);
        Assert.Equal(30m, definition.SignalEmaHysteresisBasisPoints);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, -1)]
    [InlineData(2, 1001)]
    public void HysteresisMustRespectVersionAndBounds(int version, decimal basisPoints)
    {
        var action = () => StrategyDefinition.Create(
            "invalid-hysteresis",
            version,
            Instrument,
            SignalTimeframe,
            TrendTimeframe,
            20,
            200,
            2m,
            200,
            200,
            basisPoints);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void TrendTimeframeMustBeLargerExactSignalMultiple()
    {
        var action = () => StrategyDefinition.Create(
            "invalid-timeframes",
            1,
            Instrument,
            Timeframe.Create(TimeSpan.FromMinutes(45)),
            TrendTimeframe,
            20,
            200,
            2m,
            200,
            200);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void WarmupCannotBeShorterThanEmaPeriod()
    {
        var action = () => StrategyDefinition.Create(
            "short-warmup",
            1,
            Instrument,
            SignalTimeframe,
            TrendTimeframe,
            20,
            200,
            2m,
            20,
            200);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void FomoGuardMustUseBoundedPositivePercent(decimal maximumMovePercent)
    {
        var action = () => StrategyDefinition.Create(
            "invalid-fomo-guard",
            1,
            Instrument,
            SignalTimeframe,
            TrendTimeframe,
            20,
            200,
            maximumMovePercent,
            200,
            200);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void DecisionCarriesVersionAndOnlyLongFlatAction()
    {
        var decision = StrategyDecision.Create(
            Definition(),
            StrategyAction.EnterLong,
            SignalCandle(),
            TrendCandle(),
            EvaluationTime,
            "trend-confirmed");

        Assert.Equal(StrategyAction.EnterLong, decision.Action);
        Assert.Equal(1, decision.StrategyVersion);
        Assert.Equal("trend-confirmed", decision.ReasonCode);
    }

    [Fact]
    public void OpenSignalCandleCannotProduceDecision()
    {
        var action = () => StrategyDecision.Create(
            Definition(),
            StrategyAction.Hold,
            SignalCandle(openTime: EvaluationTime),
            TrendCandle(),
            EvaluationTime,
            "waiting-for-close");

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void FutureTrendCandleCannotLeakIntoSignalDecision()
    {
        var action = () => StrategyDecision.Create(
            Definition(),
            StrategyAction.EnterLong,
            SignalCandle(),
            TrendCandle(openTime: EvaluationTime.AddMinutes(-15)),
            EvaluationTime,
            "future-trend-data");

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void CandleIdentityMustMatchDefinition()
    {
        var otherInstrument = InstrumentId.Create("OKX", "ETH-USDT");
        var action = () => StrategyDecision.Create(
            Definition(),
            StrategyAction.ExitToFlat,
            SignalCandle(instrumentId: otherInstrument),
            TrendCandle(),
            EvaluationTime,
            "identity-mismatch");

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void UndefinedActionIsRejected()
    {
        var action = () => StrategyDecision.Create(
            Definition(),
            (StrategyAction)999,
            SignalCandle(),
            TrendCandle(),
            EvaluationTime,
            "invalid-action");

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static StrategyDefinition Definition() =>
        StrategyDefinition.Create(
            "btc-usdt-long-flat-baseline",
            1,
            Instrument,
            SignalTimeframe,
            TrendTimeframe,
            20,
            200,
            2m,
            200,
            200);

    private static Candle SignalCandle(
        DateTimeOffset? openTime = null,
        InstrumentId? instrumentId = null) =>
        Candle.CreateClosed(
            instrumentId ?? Instrument,
            SignalTimeframe,
            openTime ?? EvaluationTime.AddMinutes(-15),
            EvaluationTime.AddMinutes(15),
            100m,
            101m,
            99m,
            100m,
            1m);

    private static Candle TrendCandle(DateTimeOffset? openTime = null) =>
        Candle.CreateClosed(
            Instrument,
            TrendTimeframe,
            openTime ?? EvaluationTime.AddHours(-1).AddMinutes(-15),
            EvaluationTime.AddHours(1),
            100m,
            101m,
            99m,
            100m,
            1m);
}
