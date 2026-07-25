using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Tests;

public sealed class PaperExecutionEngineTests
{
    private static readonly DateTimeOffset SubmittedAt = new(2026, 7, 25, 20, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly OrderId OrderId = OrderId.From(Guid.Parse("c692e1f0-3125-4d31-975c-dce173d364e2"));

    [Fact]
    public void FillWaitsUntilConfiguredLatencyHasElapsed()
    {
        var result = Evaluate(
            Request(OrderSide.Buy, OrderType.Market, 1m),
            Market(ask: 100m, askQuantity: 10m, occurredAt: SubmittedAt.AddMilliseconds(99)));

        Assert.Equal(PaperExecutionStatus.WaitingForLatency, result.Status);
        Assert.Null(result.Fill);
    }

    [Fact]
    public void MarketBuyUsesAskPlusSlippageAndCommission()
    {
        var result = Evaluate(
            Request(OrderSide.Buy, OrderType.Market, 1m),
            Market(ask: 100m, askQuantity: 10m));

        var fill = Assert.IsType<PaperFill>(result.Fill);
        Assert.Equal(100.1m, fill.Price.Value);
        Assert.Equal(0.1001m, fill.QuoteFee.Amount);
    }

    [Fact]
    public void MarketSellUsesBidMinusSlippage()
    {
        var result = Evaluate(
            Request(OrderSide.Sell, OrderType.Market, 1m),
            Market(bid: 100m, bidQuantity: 10m));

        Assert.Equal(99.9m, Assert.IsType<PaperFill>(result.Fill).Price.Value);
    }

    [Fact]
    public void BuyLimitWaitsWhenSlippageAdjustedPriceExceedsLimit()
    {
        var result = Evaluate(
            Request(OrderSide.Buy, OrderType.Limit, 1m, limit: 100m),
            Market(ask: 100m, askQuantity: 10m));

        Assert.Equal(PaperExecutionStatus.WaitingForLimitPrice, result.Status);
    }

    [Fact]
    public void SellLimitFillsOnlyAtOrAboveLimit()
    {
        var result = Evaluate(
            Request(OrderSide.Sell, OrderType.Limit, 1m, limit: 99m),
            Market(bid: 100m, bidQuantity: 10m));

        Assert.Equal(PaperExecutionStatus.Filled, result.Status);
    }

    [Fact]
    public void LiquidityParticipationProducesPartialFill()
    {
        var result = Evaluate(
            Request(OrderSide.Buy, OrderType.Market, 5m),
            Market(ask: 100m, askQuantity: 2m));

        Assert.Equal(0.5m, Assert.IsType<PaperFill>(result.Fill).Quantity.Value);
    }

    [Fact]
    public void EmptyTopOfBookWaitsForLiquidity()
    {
        var result = Evaluate(
            Request(OrderSide.Buy, OrderType.Market, 1m),
            Market(ask: 100m, askQuantity: 0m));

        Assert.Equal(PaperExecutionStatus.WaitingForLiquidity, result.Status);
    }

    [Fact]
    public void SameInputsProduceSameFill()
    {
        var engine = new PaperExecutionEngine();
        var policy = Policy();
        var request = Request(OrderSide.Buy, OrderType.Market, 1m);
        var market = Market(ask: 100m, askQuantity: 10m);

        var first = engine.Evaluate(policy, request, market);
        var second = engine.Evaluate(policy, request, market);

        Assert.Equal(first, second);
    }

    private static PaperExecutionResult Evaluate(
        PaperExecutionRequest request,
        PaperTopOfBookSnapshot market) =>
        new PaperExecutionEngine().Evaluate(Policy(), request, market);

    private static PaperExecutionPolicy Policy() =>
        new(
            TimeSpan.FromMilliseconds(100),
            Percentage.FromPercent(0.1m),
            SlippageBasisPoints: 10m,
            Percentage.FromPercent(25m));

    private static PaperExecutionRequest Request(
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? limit = null) =>
        new(
            OrderId,
            Instrument,
            AssetCode.Create("USDT"),
            side,
            type,
            Quantity.From(quantity),
            limit is null ? null : Price.From(limit.Value),
            SubmittedAt);

    private static PaperTopOfBookSnapshot Market(
        decimal bid = 99m,
        decimal bidQuantity = 10m,
        decimal ask = 100m,
        decimal askQuantity = 10m,
        DateTimeOffset? occurredAt = null) =>
        new(
            Instrument,
            Price.From(bid),
            bidQuantity,
            Price.From(ask),
            askQuantity,
            occurredAt ?? SubmittedAt.AddMilliseconds(100));
}
