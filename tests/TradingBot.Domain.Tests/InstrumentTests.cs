using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Tests;

public sealed class InstrumentTests
{
    private static readonly Instrument BtcUsdt = Instrument.Create(
        InstrumentId.Create("test", "btcusdt"),
        priceTickSize: 0.10m,
        quantityStepSize: 0.001m,
        minimumQuantity: 0.001m,
        minimumNotional: 5m);

    [Fact]
    public void NormalizePrice_FloorsToTickSize()
    {
        var result = BtcUsdt.NormalizePrice(Price.From(10_000.19m));

        Assert.Equal(10_000.10m, result.Value);
    }

    [Fact]
    public void NormalizePriceUp_CeilsToTickSize()
    {
        var result = BtcUsdt.NormalizePriceUp(Price.From(10_000.11m));

        Assert.Equal(10_000.20m, result.Value);
    }

    [Fact]
    public void NormalizeQuantity_FloorsToStepSize()
    {
        var result = BtcUsdt.NormalizeQuantity(Quantity.From(0.0019m));

        Assert.Equal(0.001m, result.Value);
    }

    [Fact]
    public void EnsureTradable_RejectsNotionalBelowMinimum()
    {
        var action = () => BtcUsdt.EnsureTradable(Price.From(1_000m), Quantity.From(0.001m));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void EnsureTradable_AcceptsValidOrder()
    {
        BtcUsdt.EnsureTradable(Price.From(10_000m), Quantity.From(0.001m));
    }
}
