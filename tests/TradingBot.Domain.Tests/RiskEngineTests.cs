using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;

namespace TradingBot.Domain.Tests;

public sealed class RiskEngineTests
{
    private static readonly Instrument BtcUsdt = Instrument.Create(
        InstrumentId.Create("test", "BTCUSDT"),
        priceTickSize: 0.10m,
        quantityStepSize: 0.001m,
        minimumQuantity: 0.001m,
        minimumNotional: 5m);

    private static readonly RiskProfile Profile = RiskProfile.Create(
        Percentage.FromPercent(1m),
        Percentage.FromPercent(5m),
        Money.Create(5_000m, "USDT"),
        Money.Create(10_000m, "USDT"),
        maximumOpenOrders: 5);

    private readonly RiskEngine _engine = new();

    [Fact]
    public void Evaluate_ApprovesRequestWithinAllLimits()
    {
        var decision = _engine.Evaluate(Profile, CreateRequest(requestedQuantity: 0.05m));

        Assert.Equal(RiskDecisionType.Approved, decision.Type);
        Assert.Equal(0.05m, decision.ApprovedQuantity?.Value);
    }

    [Fact]
    public void Evaluate_ResizesByMaximumRiskPerTrade()
    {
        var decision = _engine.Evaluate(
            Profile,
            CreateRequest(requestedQuantity: 20m, entryPrice: 100m, stopPrice: 90m));

        Assert.Equal(RiskDecisionType.Resized, decision.Type);
        Assert.Equal(10m, decision.ApprovedQuantity?.Value);
    }

    [Fact]
    public void Evaluate_ResizesBySymbolExposureCapacity()
    {
        var decision = _engine.Evaluate(
            Profile,
            CreateRequest(
                requestedQuantity: 1m,
                entryPrice: 10_000m,
                stopPrice: 9_990m,
                currentSymbolExposure: 4_000m,
                currentGrossExposure: 4_000m));

        Assert.Equal(RiskDecisionType.Resized, decision.Type);
        Assert.Equal(0.1m, decision.ApprovedQuantity?.Value);
    }

    [Fact]
    public void Evaluate_RejectsWhenKillSwitchIsActive()
    {
        var decision = _engine.Evaluate(Profile, CreateRequest(isKillSwitchActive: true));

        Assert.Equal(RiskDecisionType.Rejected, decision.Type);
        Assert.Equal(RiskRejectionCode.KillSwitchActive, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsStaleMarketData()
    {
        var decision = _engine.Evaluate(Profile, CreateRequest(isMarketDataFresh: false));

        Assert.Equal(RiskRejectionCode.StaleMarketData, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsAtDailyLossLimit()
    {
        var decision = _engine.Evaluate(Profile, CreateRequest(dailyPnl: -500m));

        Assert.Equal(RiskRejectionCode.DailyLossLimitReached, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsAtMaximumOpenOrders()
    {
        var decision = _engine.Evaluate(Profile, CreateRequest(openOrderCount: 5));

        Assert.Equal(RiskRejectionCode.MaximumOpenOrdersReached, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsWhenSymbolExposureIsFull()
    {
        var decision = _engine.Evaluate(
            Profile,
            CreateRequest(currentSymbolExposure: 5_000m, currentGrossExposure: 5_000m));

        Assert.Equal(RiskRejectionCode.MaximumSymbolExposureReached, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsWhenAdjustedQuantityIsBelowMinimumNotional()
    {
        var restrictiveProfile = RiskProfile.Create(
            Percentage.FromPercent(0.001m),
            Percentage.FromPercent(5m),
            Money.Create(5_000m, "USDT"),
            Money.Create(10_000m, "USDT"),
            maximumOpenOrders: 5);

        var decision = _engine.Evaluate(
            restrictiveProfile,
            CreateRequest(requestedQuantity: 1m, entryPrice: 100m, stopPrice: 90m));

        Assert.Equal(RiskRejectionCode.BelowTradingMinimum, decision.RejectionCode);
    }

    [Fact]
    public void Evaluate_RejectsInvalidStopDirection()
    {
        var request = CreateRequest(entryPrice: 100m, stopPrice: 101m);

        var action = () => _engine.Evaluate(Profile, request);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void Evaluate_RejectsCurrencyMismatch()
    {
        var request = CreateRequest() with { AccountEquity = Money.Create(10_000m, "USD") };

        var action = () => _engine.Evaluate(Profile, request);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static RiskEvaluationRequest CreateRequest(
        decimal requestedQuantity = 0.05m,
        decimal entryPrice = 10_000m,
        decimal stopPrice = 9_900m,
        decimal dailyPnl = 0m,
        decimal currentSymbolExposure = 0m,
        decimal currentGrossExposure = 0m,
        int openOrderCount = 0,
        bool isKillSwitchActive = false,
        bool isMarketDataFresh = true) =>
        new(
            BtcUsdt,
            OrderSide.Buy,
            Quantity.From(requestedQuantity),
            Price.From(entryPrice),
            Price.From(stopPrice),
            Money.Create(10_000m, "USDT"),
            Money.Create(dailyPnl, "USDT"),
            Money.Create(currentSymbolExposure, "USDT"),
            Money.Create(currentGrossExposure, "USDT"),
            openOrderCount,
            isKillSwitchActive,
            isMarketDataFresh);
}
