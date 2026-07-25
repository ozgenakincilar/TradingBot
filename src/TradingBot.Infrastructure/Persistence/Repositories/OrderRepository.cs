using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository(TradingBotDbContext context) : IOrderRepository
{
    public Task<bool> ExistsAsync(
        ClientOrderId clientOrderId,
        CancellationToken cancellationToken) =>
        context.Orders
            .AsNoTracking()
            .AnyAsync(
                order => order.ClientOrderId == clientOrderId.Value,
                cancellationToken);

    public async Task<Order?> GetAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var entity = await context.Orders.FindAsync([orderId.Value], cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public void Add(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        context.Orders.Add(new ExecutionOrderEntity
        {
            Id = order.Id.Value,
            ClientOrderId = order.ClientOrderId.Value,
            Exchange = order.InstrumentId.Exchange,
            Symbol = order.InstrumentId.Symbol,
            Side = (byte)order.Side,
            Type = (byte)order.Type,
            Status = (byte)order.Status,
            RequestedQuantity = order.RequestedQuantity.Value,
            ApprovedQuantity = order.ApprovedQuantity.Value,
            LimitPrice = order.LimitPrice?.Value,
            FilledQuantity = order.FilledQuantity,
            AverageFillPrice = order.AverageFillPrice,
            ExchangeOrderId = order.ExchangeOrderId,
            RejectionReason = order.RejectionReason,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        });
    }

    public void Store(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var entity = context.Orders.Local.SingleOrDefault(candidate => candidate.Id == order.Id.Value)
            ?? throw new InvalidOperationException("Order must be loaded before it can be stored.");
        Apply(entity, order);
    }

    private static Order Map(ExecutionOrderEntity entity) =>
        Order.Restore(
            OrderId.From(entity.Id),
            ClientOrderId.Create(entity.ClientOrderId),
            InstrumentId.Create(entity.Exchange, entity.Symbol),
            (OrderSide)entity.Side,
            (OrderType)entity.Type,
            Quantity.From(entity.RequestedQuantity),
            Quantity.From(entity.ApprovedQuantity),
            entity.LimitPrice is null ? null : Price.From(entity.LimitPrice.Value),
            (OrderStatus)entity.Status,
            entity.FilledQuantity,
            entity.AverageFillPrice,
            entity.ExchangeOrderId,
            entity.RejectionReason,
            entity.CreatedAt,
            entity.UpdatedAt);

    private static void Apply(ExecutionOrderEntity entity, Order order)
    {
        entity.Status = (byte)order.Status;
        entity.ApprovedQuantity = order.ApprovedQuantity.Value;
        entity.FilledQuantity = order.FilledQuantity;
        entity.AverageFillPrice = order.AverageFillPrice;
        entity.ExchangeOrderId = order.ExchangeOrderId;
        entity.RejectionReason = order.RejectionReason;
        entity.UpdatedAt = order.UpdatedAt;
    }
}
