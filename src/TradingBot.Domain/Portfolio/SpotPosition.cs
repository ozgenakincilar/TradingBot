using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Portfolio;

public sealed class SpotPosition
{
    private SpotPosition(
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        DateTimeOffset updatedAt)
    {
        InstrumentId = instrumentId;
        BaseAsset = baseAsset;
        QuoteAsset = quoteAsset;
        UpdatedAt = updatedAt;
    }

    public InstrumentId InstrumentId { get; }

    public AssetCode BaseAsset { get; }

    public AssetCode QuoteAsset { get; }

    public decimal OpenQuantity { get; private set; }

    public decimal ReservedSellQuantity { get; private set; }

    public decimal AvailableQuantity => OpenQuantity - ReservedSellQuantity;

    public decimal AverageEntryPrice { get; private set; }

    public decimal RealizedPnl { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SpotPosition Open(
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        DateTimeOffset openedAt)
    {
        if (instrumentId == default)
        {
            throw new ArgumentException("Instrument id is required.", nameof(instrumentId));
        }

        if (baseAsset == default || quoteAsset == default)
        {
            throw new ArgumentException("Base and quote assets are required.");
        }

        if (baseAsset == quoteAsset)
        {
            throw new DomainRuleViolationException("Base and quote assets must be different.");
        }

        return new SpotPosition(instrumentId, baseAsset, quoteAsset, openedAt);
    }

    public void ApplyBuyFill(
        Quantity quantity,
        Price price,
        Money quoteFee,
        DateTimeOffset occurredAt)
    {
        ValidateFee(quoteFee);
        EnsureEventTime(occurredAt);

        var previousCost = AverageEntryPrice * OpenQuantity;
        var fillCost = price.Value * quantity.Value + quoteFee.Amount;
        OpenQuantity += quantity.Value;
        AverageEntryPrice = (previousCost + fillCost) / OpenQuantity;
        UpdatedAt = occurredAt;
    }

    public void ReserveForSell(Quantity quantity, DateTimeOffset occurredAt)
    {
        EnsureCanReserveForSell(quantity);
        EnsureEventTime(occurredAt);
        ReservedSellQuantity += quantity.Value;
        UpdatedAt = occurredAt;
    }

    public void ReleaseSellReservation(Quantity quantity, DateTimeOffset occurredAt)
    {
        EnsureEventTime(occurredAt);

        if (quantity.Value > ReservedSellQuantity)
        {
            throw new DomainRuleViolationException(
                "Cannot release more quantity than the position has reserved.");
        }

        ReservedSellQuantity -= quantity.Value;
        UpdatedAt = occurredAt;
    }

    public Money ApplySellFill(
        Quantity quantity,
        Price price,
        Money quoteFee,
        DateTimeOffset occurredAt)
    {
        ValidateFee(quoteFee);
        EnsureEventTime(occurredAt);

        if (quantity.Value > ReservedSellQuantity)
        {
            throw new DomainRuleViolationException(
                "Sell fill cannot exceed the position's reserved quantity.");
        }

        var netProceeds = price.Value * quantity.Value - quoteFee.Amount;
        if (netProceeds <= 0m)
        {
            throw new DomainRuleViolationException("Sell proceeds must remain positive after fees.");
        }

        var realized = netProceeds - AverageEntryPrice * quantity.Value;
        RealizedPnl += realized;
        ReservedSellQuantity -= quantity.Value;
        OpenQuantity -= quantity.Value;

        if (OpenQuantity == 0m)
        {
            AverageEntryPrice = 0m;
        }

        UpdatedAt = occurredAt;
        return Money.Create(realized, QuoteAsset.Value);
    }

    public Money CalculateUnrealizedPnl(Price markPrice) =>
        Money.Create(
            (markPrice.Value - AverageEntryPrice) * OpenQuantity,
            QuoteAsset.Value);

    public void EnsureCanReserveForSell(Quantity quantity)
    {
        if (quantity.Value > AvailableQuantity)
        {
            throw new DomainRuleViolationException(
                $"Cannot reserve {quantity.Value} {BaseAsset}; available position is {AvailableQuantity}.");
        }
    }

    private void ValidateFee(Money fee)
    {
        if (fee.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(fee), "Fee cannot be negative.");
        }

        if (!string.Equals(fee.Currency, QuoteAsset.Value, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"Fee must be denominated in quote asset {QuoteAsset}.");
        }
    }

    public void EnsureEventTime(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new DomainRuleViolationException("Position events cannot move backwards in time.");
        }
    }
}
