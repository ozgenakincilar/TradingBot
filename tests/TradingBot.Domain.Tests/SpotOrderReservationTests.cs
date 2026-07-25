using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Tests;

public sealed class SpotOrderReservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly OrderId OrderId = OrderId.From(Guid.Parse("12e65211-cdea-4358-bd9b-f9e1ad043fd6"));
    private static readonly InstrumentId Instrument = InstrumentId.Create("TEST", "BTCUSDT");
    private static readonly AssetCode Btc = AssetCode.Create("BTC");
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");

    [Fact]
    public void BuyPartialFillKeepsUnusedQuoteReserved()
    {
        var reservation = CreateBuy(quantity: 2m, price: 100m, fee: 2m);

        var released = reservation.ApplyBuyFill(
            Quantity.From(1m),
            Money.Create(91m, "USDT"),
            Now.AddSeconds(1));

        Assert.Equal(0m, released);
        Assert.Equal(111m, reservation.RemainingReserved);
        Assert.Equal(SpotReservationStatus.Active, reservation.Status);
    }

    [Fact]
    public void FinalBuyFillReleasesPriceImprovementSurplus()
    {
        var reservation = CreateBuy(quantity: 1m, price: 100m, fee: 1m);

        var released = reservation.ApplyBuyFill(
            Quantity.From(1m),
            Money.Create(91m, "USDT"),
            Now.AddSeconds(1));

        Assert.Equal(10m, released);
        Assert.Equal(0m, reservation.RemainingReserved);
        Assert.Equal(SpotReservationStatus.Filled, reservation.Status);
    }

    [Fact]
    public void CancelReturnsOnlyRemainingReservation()
    {
        var reservation = CreateBuy(quantity: 2m, price: 100m, fee: 2m);
        reservation.ApplyBuyFill(Quantity.From(1m), Money.Create(91m, "USDT"), Now.AddSeconds(1));

        var released = reservation.Cancel(Now.AddSeconds(2));

        Assert.Equal(111m, released);
        Assert.Equal(SpotReservationStatus.Cancelled, reservation.Status);
    }

    [Fact]
    public void ClosedReservationRejectsLateFill()
    {
        var reservation = CreateBuy(quantity: 1m, price: 100m, fee: 1m);
        reservation.Cancel(Now.AddSeconds(1));

        var action = () =>
        {
            reservation.ApplyBuyFill(
                Quantity.From(1m),
                Money.Create(101m, "USDT"),
                Now.AddSeconds(2));
        };

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void PartialBuyFillCannotExhaustAllQuoteReservation()
    {
        var reservation = CreateBuy(quantity: 2m, price: 100m, fee: 0m);

        var action = () =>
        {
            reservation.ApplyBuyFill(
                Quantity.From(1m),
                Money.Create(200m, "USDT"),
                Now.AddSeconds(1));
        };

        Assert.Throws<DomainRuleViolationException>(action);
        Assert.Equal(0m, reservation.FilledQuantity);
        Assert.Equal(200m, reservation.RemainingReserved);
    }

    [Fact]
    public void SellFillCannotExceedRemainingBaseReservation()
    {
        var reservation = SpotOrderReservation.ReserveSell(
            OrderId,
            Instrument,
            Btc,
            Usdt,
            Quantity.From(1m),
            Now);

        var action = () =>
        {
            reservation.ApplySellFill(Quantity.From(1.01m), Now.AddSeconds(1));
        };

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static SpotOrderReservation CreateBuy(decimal quantity, decimal price, decimal fee) =>
        SpotOrderReservation.ReserveBuy(
            OrderId,
            Instrument,
            Btc,
            Usdt,
            Quantity.From(quantity),
            Price.From(price),
            Money.Create(fee, "USDT"),
            Now);
}
