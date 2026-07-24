using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;

namespace TradingBot.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_LimitOrderWithoutPrice_Throws()
    {
        var action = () => Order.Create(
            OrderId.New(),
            ClientOrderId.Create("BOT-INVALID-0001"),
            InstrumentId.Create("TEST", "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(1m),
            limitPrice: null,
            Now);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ApproveRisk_CannotIncreaseQuantity()
    {
        var order = CreateOrder();

        var action = () => order.ApproveRisk(Quantity.From(2m), Now.AddSeconds(1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void Submit_RequiresRiskApproval()
    {
        var order = CreateOrder();

        var action = () => order.MarkSubmitting(Now.AddSeconds(1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void SubmissionTimeout_BecomesUnknown_AndCanBeReconciledAsOpen()
    {
        var order = CreateOrder();
        order.ApproveRisk(Quantity.From(1m), Now.AddSeconds(1));
        order.MarkSubmitting(Now.AddSeconds(2));

        order.MarkSubmissionUnknown(Now.AddSeconds(3));
        order.MarkAccepted("exchange-42", Now.AddSeconds(4));

        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Equal("exchange-42", order.ExchangeOrderId);
    }

    [Fact]
    public void PartialFills_CalculateWeightedAverage_AndFinishOrder()
    {
        var order = CreateOpenOrder();

        order.ApplyFill(Quantity.From(0.25m), Price.From(100m), Now.AddSeconds(3));
        order.ApplyFill(Quantity.From(0.75m), Price.From(104m), Now.AddSeconds(4));

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(1m, order.FilledQuantity);
        Assert.Equal(103m, order.AverageFillPrice);
    }

    [Fact]
    public void Fill_CannotExceedApprovedQuantity()
    {
        var order = CreateOpenOrder();

        var action = () => order.ApplyFill(
            Quantity.From(1.01m),
            Price.From(100m),
            Now.AddSeconds(3));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void FillCanWinCancellationRace()
    {
        var order = CreateOpenOrder();
        order.RequestCancellation(Now.AddSeconds(3));

        order.ApplyFill(Quantity.From(1m), Price.From(100m), Now.AddSeconds(4));

        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public void TerminalOrder_CannotReturnToActiveState()
    {
        var order = CreateOpenOrder();
        order.RequestCancellation(Now.AddSeconds(3));
        order.MarkCancelled(Now.AddSeconds(4));

        var action = () => order.MarkAccepted("another-id", Now.AddSeconds(5));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void EventTime_CannotMoveBackwards()
    {
        var order = CreateOrder();

        var action = () => order.ApproveRisk(Quantity.From(1m), Now.AddSeconds(-1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static Order CreateOpenOrder()
    {
        var order = CreateOrder();
        order.ApproveRisk(Quantity.From(1m), Now.AddSeconds(1));
        order.MarkSubmitting(Now.AddSeconds(2));
        order.MarkAccepted("exchange-1", Now.AddSeconds(2));
        return order;
    }

    private static Order CreateOrder(
        OrderType type = OrderType.Limit,
        Price? limitPrice = null) =>
        Order.Create(
            OrderId.From(Guid.Parse("558b4e7b-6209-4c4e-b017-e91832c62ad4")),
            ClientOrderId.Create("BOT-STRATEGY-0001"),
            InstrumentId.Create("TEST", "BTCUSDT"),
            OrderSide.Buy,
            type,
            Quantity.From(1m),
            type == OrderType.Limit ? limitPrice ?? Price.From(100m) : limitPrice,
            Now);
}
