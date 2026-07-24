using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
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
}
