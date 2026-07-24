using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Tests;

public sealed class SpotTradeSettlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly AssetCode Btc = AssetCode.Create("BTC");
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");
    private readonly SpotTradeSettlementService _settlement = new();

    [Fact]
    public void Buy_ConsumesReservedQuoteAndCreditsBaseAsset()
    {
        var state = CreateState(quoteTotal: 1_000m);

        _settlement.ReserveBuy(
            state.Quote,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(1m, "USDT"),
            Now.AddSeconds(1));
        _settlement.SettleBuy(
            state.Quote,
            state.Base,
            state.Position,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(1m, "USDT"),
            Now.AddSeconds(2));

        Assert.Equal(899m, state.Quote.Total);
        Assert.Equal(0m, state.Quote.Reserved);
        Assert.Equal(1m, state.Base.Total);
        Assert.Equal(1m, state.Position.OpenQuantity);
        Assert.Equal(101m, state.Position.AverageEntryPrice);
    }

    [Fact]
    public void MultipleBuys_CalculateFeeAdjustedWeightedAverageCost()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 1m, price: 100m, fee: 1m, second: 1);
        Buy(state, quantity: 1m, price: 120m, fee: 1m, second: 3);

        Assert.Equal(2m, state.Position.OpenQuantity);
        Assert.Equal(111m, state.Position.AverageEntryPrice);
    }

    [Fact]
    public void Sell_UsesReservedInventoryAndCalculatesNetRealizedPnl()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 1m, price: 100m, fee: 1m, second: 1);

        _settlement.ReserveSell(
            state.Base,
            state.Position,
            Quantity.From(0.5m),
            Now.AddSeconds(3));
        var realized = _settlement.SettleSell(
            state.Base,
            state.Quote,
            state.Position,
            Quantity.From(0.5m),
            Price.From(130m),
            Money.Create(1m, "USDT"),
            Now.AddSeconds(4));

        Assert.Equal(13.5m, realized.Amount);
        Assert.Equal(13.5m, state.Position.RealizedPnl);
        Assert.Equal(0.5m, state.Position.OpenQuantity);
        Assert.Equal(0m, state.Position.ReservedSellQuantity);
        Assert.Equal(0.5m, state.Base.Total);
        Assert.Equal(963m, state.Quote.Total);
    }

    [Fact]
    public void Sell_CannotReserveMoreThanOwnedInventory()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 1m, price: 100m, fee: 0m, second: 1);

        var action = () => _settlement.ReserveSell(
            state.Base,
            state.Position,
            Quantity.From(1.001m),
            Now.AddSeconds(3));

        Assert.Throws<DomainRuleViolationException>(action);
        Assert.Equal(0m, state.Base.Reserved);
        Assert.Equal(0m, state.Position.ReservedSellQuantity);
    }

    [Fact]
    public void CancelledSell_ReleasesBothBalanceAndPositionReservations()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 1m, price: 100m, fee: 0m, second: 1);
        _settlement.ReserveSell(
            state.Base,
            state.Position,
            Quantity.From(0.75m),
            Now.AddSeconds(3));

        _settlement.ReleaseSell(
            state.Base,
            state.Position,
            Quantity.From(0.75m),
            Now.AddSeconds(4));

        Assert.Equal(0m, state.Base.Reserved);
        Assert.Equal(1m, state.Base.Available);
        Assert.Equal(0m, state.Position.ReservedSellQuantity);
        Assert.Equal(1m, state.Position.AvailableQuantity);
    }

    [Fact]
    public void Buy_CannotReserveMoreQuoteThanAvailable()
    {
        var state = CreateState(quoteTotal: 100m);

        var action = () => _settlement.ReserveBuy(
            state.Quote,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(1m, "USDT"),
            Now.AddSeconds(1));

        Assert.Throws<DomainRuleViolationException>(action);
        Assert.Equal(0m, state.Quote.Reserved);
    }

    [Fact]
    public void CancelledBuy_ReleasesQuoteReservation()
    {
        var state = CreateState(quoteTotal: 1_000m);
        _settlement.ReserveBuy(
            state.Quote,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(1m, "USDT"),
            Now.AddSeconds(1));

        _settlement.ReleaseBuy(
            state.Quote,
            Money.Create(101m, "USDT"),
            Now.AddSeconds(2));

        Assert.Equal(0m, state.Quote.Reserved);
        Assert.Equal(1_000m, state.Quote.Available);
    }

    [Fact]
    public void Fee_MustUseQuoteAsset()
    {
        var state = CreateState(quoteTotal: 1_000m);

        var action = () => _settlement.ReserveBuy(
            state.Quote,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(1m, "BTC"),
            Now.AddSeconds(1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void UnrealizedPnl_UsesOpenQuantityAndAverageCost()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 2m, price: 100m, fee: 2m, second: 1);

        var unrealized = state.Position.CalculateUnrealizedPnl(Price.From(120m));

        Assert.Equal(38m, unrealized.Amount);
        Assert.Equal("USDT", unrealized.Currency);
    }

    [Fact]
    public void FullSell_ClosesPositionWithoutNegativeQuantity()
    {
        var state = CreateState(quoteTotal: 1_000m);
        Buy(state, quantity: 1m, price: 100m, fee: 0m, second: 1);
        _settlement.ReserveSell(
            state.Base,
            state.Position,
            Quantity.From(1m),
            Now.AddSeconds(3));

        _settlement.SettleSell(
            state.Base,
            state.Quote,
            state.Position,
            Quantity.From(1m),
            Price.From(110m),
            Money.Create(0m, "USDT"),
            Now.AddSeconds(4));

        Assert.Equal(0m, state.Position.OpenQuantity);
        Assert.Equal(0m, state.Position.AverageEntryPrice);
        Assert.Equal(0m, state.Base.Total);
        Assert.Equal(10m, state.Position.RealizedPnl);
    }

    [Fact]
    public void Settlement_WithStaleBalanceTimeDoesNotPartiallyMutatePosition()
    {
        var state = CreateState(quoteTotal: 1_000m);
        state.Base.Credit(1m, Now.AddSeconds(5));
        state.Quote.Reserve(100m, Now.AddSeconds(1));

        var action = () => _settlement.SettleBuy(
            state.Quote,
            state.Base,
            state.Position,
            Quantity.From(1m),
            Price.From(100m),
            Money.Create(0m, "USDT"),
            Now.AddSeconds(2));

        Assert.Throws<DomainRuleViolationException>(action);
        Assert.Equal(0m, state.Position.OpenQuantity);
        Assert.Equal(1m, state.Base.Total);
        Assert.Equal(100m, state.Quote.Reserved);
    }

    private void Buy(
        PortfolioState state,
        decimal quantity,
        decimal price,
        decimal fee,
        int second)
    {
        var fillQuantity = Quantity.From(quantity);
        var fillPrice = Price.From(price);
        var fillFee = Money.Create(fee, "USDT");
        _settlement.ReserveBuy(state.Quote, fillQuantity, fillPrice, fillFee, Now.AddSeconds(second));
        _settlement.SettleBuy(
            state.Quote,
            state.Base,
            state.Position,
            fillQuantity,
            fillPrice,
            fillFee,
            Now.AddSeconds(second + 1));
    }

    private static PortfolioState CreateState(decimal quoteTotal) =>
        new(
            AssetBalance.Create(Usdt, quoteTotal, 0m, Now),
            AssetBalance.Create(Btc, 0m, 0m, Now),
            SpotPosition.Open(
                InstrumentId.Create("TEST", "BTCUSDT"),
                Btc,
                Usdt,
                Now));

    private sealed record PortfolioState(
        AssetBalance Quote,
        AssetBalance Base,
        SpotPosition Position);
}
