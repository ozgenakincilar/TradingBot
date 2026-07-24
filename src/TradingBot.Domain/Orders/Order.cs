using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Orders;

public sealed class Order
{
    private Order(
        OrderId id,
        ClientOrderId clientOrderId,
        InstrumentId instrumentId,
        OrderSide side,
        OrderType type,
        Quantity requestedQuantity,
        Price? limitPrice,
        DateTimeOffset createdAt)
    {
        Id = id;
        ClientOrderId = clientOrderId;
        InstrumentId = instrumentId;
        Side = side;
        Type = type;
        RequestedQuantity = requestedQuantity;
        ApprovedQuantity = requestedQuantity;
        LimitPrice = limitPrice;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Status = OrderStatus.Draft;
    }

    public OrderId Id { get; }

    public ClientOrderId ClientOrderId { get; }

    public InstrumentId InstrumentId { get; }

    public OrderSide Side { get; }

    public OrderType Type { get; }

    public Quantity RequestedQuantity { get; }

    public Quantity ApprovedQuantity { get; private set; }

    public Price? LimitPrice { get; }

    public OrderStatus Status { get; private set; }

    public decimal FilledQuantity { get; private set; }

    public decimal? AverageFillPrice { get; private set; }

    public string? ExchangeOrderId { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Order Create(
        OrderId id,
        ClientOrderId clientOrderId,
        InstrumentId instrumentId,
        OrderSide side,
        OrderType type,
        Quantity requestedQuantity,
        Price? limitPrice,
        DateTimeOffset createdAt)
    {
        if (id == default)
        {
            throw new ArgumentException("Order id is required.", nameof(id));
        }

        if (clientOrderId == default)
        {
            throw new ArgumentException("Client order id is required.", nameof(clientOrderId));
        }

        if (instrumentId == default)
        {
            throw new ArgumentException("Instrument id is required.", nameof(instrumentId));
        }

        if (side is not (OrderSide.Buy or OrderSide.Sell))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (type is not (OrderType.Market or OrderType.Limit))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (type == OrderType.Limit && limitPrice is null)
        {
            throw new ArgumentException("Limit order requires a limit price.", nameof(limitPrice));
        }

        if (type == OrderType.Market && limitPrice is not null)
        {
            throw new ArgumentException("Market order cannot have a limit price.", nameof(limitPrice));
        }

        return new Order(
            id,
            clientOrderId,
            instrumentId,
            side,
            type,
            requestedQuantity,
            limitPrice,
            createdAt);
    }

    public void ApproveRisk(Quantity approvedQuantity, DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.Draft);

        if (approvedQuantity.Value > RequestedQuantity.Value)
        {
            throw new DomainRuleViolationException("Risk approval cannot increase requested quantity.");
        }

        ApprovedQuantity = approvedQuantity;
        TransitionTo(OrderStatus.RiskApproved, occurredAt);
    }

    public void Reject(string reason, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is not (OrderStatus.Draft or OrderStatus.Submitting or OrderStatus.Open))
        {
            ThrowInvalidTransition(OrderStatus.Rejected);
        }

        RejectionReason = reason.Trim();
        TransitionTo(OrderStatus.Rejected, occurredAt);
    }

    public void MarkSubmitting(DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.RiskApproved);
        TransitionTo(OrderStatus.Submitting, occurredAt);
    }

    public void MarkAccepted(string exchangeOrderId, DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.Submitting, OrderStatus.Unknown);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeOrderId);

        ExchangeOrderId = exchangeOrderId.Trim();
        TransitionTo(OrderStatus.Open, occurredAt);
    }

    public void MarkSubmissionUnknown(DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.Submitting);
        TransitionTo(OrderStatus.Unknown, occurredAt);
    }

    public void ApplyFill(Quantity quantity, Price price, DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.Open, OrderStatus.PartiallyFilled, OrderStatus.CancelPending);

        var newFilledQuantity = FilledQuantity + quantity.Value;
        if (newFilledQuantity > ApprovedQuantity.Value)
        {
            throw new DomainRuleViolationException("Total filled quantity cannot exceed approved quantity.");
        }

        var previousNotional = (AverageFillPrice ?? 0m) * FilledQuantity;
        var newNotional = price.Value * quantity.Value;
        FilledQuantity = newFilledQuantity;
        AverageFillPrice = (previousNotional + newNotional) / FilledQuantity;

        TransitionTo(
            FilledQuantity == ApprovedQuantity.Value
                ? OrderStatus.Filled
                : OrderStatus.PartiallyFilled,
            occurredAt);
    }

    public void RequestCancellation(DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.Open, OrderStatus.PartiallyFilled);
        TransitionTo(OrderStatus.CancelPending, occurredAt);
    }

    public void MarkCancelled(DateTimeOffset occurredAt)
    {
        EnsureStatus(OrderStatus.CancelPending);
        TransitionTo(OrderStatus.Cancelled, occurredAt);
    }

    private void EnsureStatus(params OrderStatus[] allowedStatuses)
    {
        if (!allowedStatuses.Contains(Status))
        {
            ThrowInvalidTransition(allowedStatuses[0]);
        }
    }

    private void TransitionTo(OrderStatus status, DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new DomainRuleViolationException("Order events cannot move backwards in time.");
        }

        Status = status;
        UpdatedAt = occurredAt;
    }

    private void ThrowInvalidTransition(OrderStatus target) =>
        throw new DomainRuleViolationException(
            $"Order cannot transition from {Status} to {target}.");
}
