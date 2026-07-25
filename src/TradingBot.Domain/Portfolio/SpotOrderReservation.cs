using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;

namespace TradingBot.Domain.Portfolio;

public enum SpotReservationStatus
{
    Active = 1,
    Filled = 2,
    Cancelled = 3
}

public sealed class SpotOrderReservation
{
    private SpotOrderReservation(
        OrderId orderId,
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        OrderSide side,
        decimal approvedQuantity,
        decimal remainingReserved,
        DateTimeOffset createdAt)
    {
        OrderId = orderId;
        InstrumentId = instrumentId;
        BaseAsset = baseAsset;
        QuoteAsset = quoteAsset;
        Side = side;
        ApprovedQuantity = approvedQuantity;
        RemainingReserved = remainingReserved;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = SpotReservationStatus.Active;
    }

    public OrderId OrderId { get; }

    public InstrumentId InstrumentId { get; }

    public AssetCode BaseAsset { get; }

    public AssetCode QuoteAsset { get; }

    public OrderSide Side { get; }

    public decimal ApprovedQuantity { get; }

    public decimal FilledQuantity { get; private set; }

    public decimal RemainingReserved { get; private set; }

    public SpotReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static SpotOrderReservation ReserveBuy(
        OrderId orderId,
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        Quantity approvedQuantity,
        Price reservationPrice,
        Money estimatedQuoteFee,
        DateTimeOffset createdAt)
    {
        ValidateIdentity(orderId, instrumentId, baseAsset, quoteAsset);
        EnsureQuoteCurrency(quoteAsset, estimatedQuoteFee);
        if (estimatedQuoteFee.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedQuoteFee));
        }

        return new SpotOrderReservation(
            orderId,
            instrumentId,
            baseAsset,
            quoteAsset,
            OrderSide.Buy,
            approvedQuantity.Value,
            reservationPrice.Value * approvedQuantity.Value + estimatedQuoteFee.Amount,
            createdAt);
    }

    public static SpotOrderReservation ReserveSell(
        OrderId orderId,
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        Quantity approvedQuantity,
        DateTimeOffset createdAt)
    {
        ValidateIdentity(orderId, instrumentId, baseAsset, quoteAsset);
        return new SpotOrderReservation(
            orderId,
            instrumentId,
            baseAsset,
            quoteAsset,
            OrderSide.Sell,
            approvedQuantity.Value,
            approvedQuantity.Value,
            createdAt);
    }

    public static SpotOrderReservation Restore(
        OrderId orderId,
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset,
        OrderSide side,
        decimal approvedQuantity,
        decimal filledQuantity,
        decimal remainingReserved,
        SpotReservationStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        ValidateIdentity(orderId, instrumentId, baseAsset, quoteAsset);
        if (side is not (OrderSide.Buy or OrderSide.Sell) ||
            approvedQuantity <= 0m ||
            filledQuantity < 0m ||
            filledQuantity > approvedQuantity ||
            remainingReserved < 0m ||
            updatedAt < createdAt ||
            status is < SpotReservationStatus.Active or > SpotReservationStatus.Cancelled ||
            (status == SpotReservationStatus.Active && filledQuantity == approvedQuantity) ||
            (status == SpotReservationStatus.Active && remainingReserved <= 0m) ||
            (status == SpotReservationStatus.Filled && filledQuantity != approvedQuantity) ||
            (status != SpotReservationStatus.Active && remainingReserved != 0m))
        {
            throw new DomainRuleViolationException("Persisted Spot reservation violates invariants.");
        }

        var reservation = new SpotOrderReservation(
            orderId,
            instrumentId,
            baseAsset,
            quoteAsset,
            side,
            approvedQuantity,
            remainingReserved,
            createdAt)
        {
            FilledQuantity = filledQuantity,
            Status = status,
            UpdatedAt = updatedAt
        };
        return reservation;
    }

    public decimal ApplyBuyFill(Quantity quantity, Money quoteDebit, DateTimeOffset occurredAt)
    {
        EnsureSide(OrderSide.Buy);
        EnsureActiveAndTime(occurredAt);
        EnsureQuoteCurrency(QuoteAsset, quoteDebit);
        if (quoteDebit.Amount <= 0m || quoteDebit.Amount > RemainingReserved)
        {
            throw new DomainRuleViolationException("Buy fill exceeds the remaining quote reservation.");
        }

        if (FilledQuantity + quantity.Value < ApprovedQuantity &&
            RemainingReserved - quoteDebit.Amount <= 0m)
        {
            throw new DomainRuleViolationException(
                "A partial buy fill cannot exhaust the remaining quote reservation.");
        }

        ApplyQuantity(quantity);
        RemainingReserved -= quoteDebit.Amount;
        return CompleteIfFilled();
    }

    public decimal ApplySellFill(Quantity quantity, DateTimeOffset occurredAt)
    {
        EnsureSide(OrderSide.Sell);
        EnsureActiveAndTime(occurredAt);
        if (quantity.Value > RemainingReserved)
        {
            throw new DomainRuleViolationException("Sell fill exceeds the remaining base reservation.");
        }

        ApplyQuantity(quantity);
        RemainingReserved -= quantity.Value;
        return CompleteIfFilled();
    }

    public decimal Cancel(DateTimeOffset occurredAt)
    {
        EnsureActiveAndTime(occurredAt);
        var released = RemainingReserved;
        RemainingReserved = 0m;
        Status = SpotReservationStatus.Cancelled;
        UpdatedAt = occurredAt;
        return released;
    }

    private void ApplyQuantity(Quantity quantity)
    {
        if (FilledQuantity + quantity.Value > ApprovedQuantity)
        {
            throw new DomainRuleViolationException("Fill exceeds the reservation's approved quantity.");
        }

        FilledQuantity += quantity.Value;
    }

    private decimal CompleteIfFilled()
    {
        if (FilledQuantity != ApprovedQuantity)
        {
            return 0m;
        }

        var released = RemainingReserved;
        RemainingReserved = 0m;
        Status = SpotReservationStatus.Filled;
        return released;
    }

    private void EnsureActiveAndTime(DateTimeOffset occurredAt)
    {
        if (Status != SpotReservationStatus.Active)
        {
            throw new DomainRuleViolationException("Only an active Spot reservation can change.");
        }

        if (occurredAt < UpdatedAt)
        {
            throw new DomainRuleViolationException("Reservation events cannot move backwards in time.");
        }

        UpdatedAt = occurredAt;
    }

    private void EnsureSide(OrderSide side)
    {
        if (Side != side)
        {
            throw new DomainRuleViolationException($"Reservation side must be {side}.");
        }
    }

    private static void ValidateIdentity(
        OrderId orderId,
        InstrumentId instrumentId,
        AssetCode baseAsset,
        AssetCode quoteAsset)
    {
        if (orderId == default || instrumentId == default || baseAsset == default || quoteAsset == default)
        {
            throw new ArgumentException("Reservation identity is incomplete.");
        }

        if (baseAsset == quoteAsset)
        {
            throw new DomainRuleViolationException("Base and quote assets must differ.");
        }
    }

    private static void EnsureQuoteCurrency(AssetCode quoteAsset, Money money)
    {
        if (!string.Equals(quoteAsset.Value, money.Currency, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Money must be denominated in the quote asset.");
        }
    }
}
