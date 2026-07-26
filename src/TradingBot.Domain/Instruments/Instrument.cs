using TradingBot.Domain.Common;

namespace TradingBot.Domain.Instruments;

public sealed class Instrument
{
    private Instrument(
        InstrumentId id,
        decimal priceTickSize,
        decimal quantityStepSize,
        decimal minimumQuantity,
        decimal minimumNotional)
    {
        Id = id;
        PriceTickSize = priceTickSize;
        QuantityStepSize = quantityStepSize;
        MinimumQuantity = minimumQuantity;
        MinimumNotional = minimumNotional;
    }

    public InstrumentId Id { get; }

    public decimal PriceTickSize { get; }

    public decimal QuantityStepSize { get; }

    public decimal MinimumQuantity { get; }

    public decimal MinimumNotional { get; }

    public static Instrument Create(
        InstrumentId id,
        decimal priceTickSize,
        decimal quantityStepSize,
        decimal minimumQuantity,
        decimal minimumNotional)
    {
        if (id == default)
        {
            throw new ArgumentException("Instrument id is required.", nameof(id));
        }

        EnsurePositive(priceTickSize, nameof(priceTickSize));
        EnsurePositive(quantityStepSize, nameof(quantityStepSize));
        EnsurePositive(minimumQuantity, nameof(minimumQuantity));
        EnsurePositive(minimumNotional, nameof(minimumNotional));

        return new Instrument(id, priceTickSize, quantityStepSize, minimumQuantity, minimumNotional);
    }

    public Price NormalizePrice(Price price) => Price.From(FloorToStep(price.Value, PriceTickSize));

    public Price NormalizePriceUp(Price price) =>
        Price.From(CeilingToStep(price.Value, PriceTickSize));

    public Quantity NormalizeQuantity(Quantity quantity) =>
        Quantity.From(FloorToStep(quantity.Value, QuantityStepSize));

    public void EnsureTradable(Price price, Quantity quantity)
    {
        var normalizedPrice = NormalizePrice(price);
        var normalizedQuantity = NormalizeQuantity(quantity);

        if (normalizedQuantity.Value < MinimumQuantity)
        {
            throw new DomainRuleViolationException(
                $"Quantity {normalizedQuantity.Value} is below minimum {MinimumQuantity} for {Id}.");
        }

        var notional = normalizedPrice.Value * normalizedQuantity.Value;
        if (notional < MinimumNotional)
        {
            throw new DomainRuleViolationException(
                $"Notional {notional} is below minimum {MinimumNotional} for {Id}.");
        }
    }

    private static decimal FloorToStep(decimal value, decimal step) =>
        Math.Floor(value / step) * step;

    private static decimal CeilingToStep(decimal value, decimal step) =>
        Math.Ceiling(value / step) * step;

    private static void EnsurePositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value must be greater than zero.");
        }
    }
}
