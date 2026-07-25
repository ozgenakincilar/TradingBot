using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Reconciliation;

namespace TradingBot.Domain.Tests;

public sealed class SpotReconciliationEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 18, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("TEST", "BTCUSDT");
    private static readonly ClientOrderId ClientOrderId = ClientOrderId.Create("BOT-REC-1");

    [Fact]
    public void EqualAccountStatesAreConsistent()
    {
        var result = new SpotReconciliationEngine().Compare(
            Snapshot(canTrade: true, total: 100m, reserved: 10m, includeOrder: true),
            Local(total: 100m, reserved: 10m, includeOrder: true));

        Assert.True(result.IsConsistent);
        Assert.False(result.ShouldHaltTrading);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public void BalanceMismatchHaltsTrading()
    {
        var result = new SpotReconciliationEngine().Compare(
            Snapshot(canTrade: true, total: 99m, reserved: 10m, includeOrder: false),
            Local(total: 100m, reserved: 10m, includeOrder: false));

        Assert.False(result.IsConsistent);
        Assert.True(result.ShouldHaltTrading);
        Assert.Contains(result.Discrepancies, x =>
            x.Type == ReconciliationDiscrepancyType.BalanceTotalMismatch);
    }

    [Fact]
    public void DisabledExchangeAccountHaltsTrading()
    {
        var result = new SpotReconciliationEngine().Compare(
            Snapshot(canTrade: false, total: 100m, reserved: 0m, includeOrder: false),
            Local(total: 100m, reserved: 0m, includeOrder: false));

        Assert.Contains(result.Discrepancies, x =>
            x.Type == ReconciliationDiscrepancyType.AccountTradingDisabled);
    }

    [Fact]
    public void MissingExchangeOrderHaltsTrading()
    {
        var result = new SpotReconciliationEngine().Compare(
            Snapshot(canTrade: true, total: 100m, reserved: 10m, includeOrder: false),
            Local(total: 100m, reserved: 10m, includeOrder: true));

        Assert.Contains(result.Discrepancies, x =>
            x.Type == ReconciliationDiscrepancyType.OrderMissingOnExchange);
    }

    [Fact]
    public void ConfiguredToleranceIgnoresBoundedBalanceDifference()
    {
        var result = new SpotReconciliationEngine().Compare(
            Snapshot(canTrade: true, total: 100.0001m, reserved: 0m, includeOrder: false),
            Local(total: 100m, reserved: 0m, includeOrder: false),
            balanceTolerance: 0.001m);

        Assert.True(result.IsConsistent);
    }

    private static SpotAccountSnapshot Snapshot(
        bool canTrade,
        decimal total,
        decimal reserved,
        bool includeOrder) =>
        new(
            "TEST",
            "snapshot-1",
            canTrade,
            Now,
            [new ReconciliationBalance(AssetCode.Create("USDT"), total, reserved)],
            includeOrder ? [Order()] : []);

    private static LocalSpotAccountState Local(
        decimal total,
        decimal reserved,
        bool includeOrder) =>
        new(
            "TEST",
            [new ReconciliationBalance(AssetCode.Create("USDT"), total, reserved)],
            includeOrder ? [Order()] : []);

    private static ReconciliationOrder Order() =>
        new(ClientOrderId, "exchange-1", Instrument, OrderSide.Buy, 0m);
}
