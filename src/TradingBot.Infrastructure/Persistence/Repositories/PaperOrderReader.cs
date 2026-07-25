using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class PaperOrderReader(TradingBotDbContext context) : IPaperOrderReader
{
    public async Task<IReadOnlyCollection<OrderId>> GetActiveOrderIdsAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        var activeStatuses = new byte[]
        {
            (byte)OrderStatus.Open,
            (byte)OrderStatus.PartiallyFilled,
            (byte)OrderStatus.CancelPending
        };
        var ids = await (
            from order in context.Orders.AsNoTracking()
            join reservation in context.SpotOrderReservations.AsNoTracking()
                on order.Id equals reservation.OrderId
            where order.Exchange == instrumentId.Exchange &&
                  order.Symbol == instrumentId.Symbol &&
                  activeStatuses.Contains(order.Status) &&
                  reservation.Status == (byte)SpotReservationStatus.Active
            orderby order.CreatedAt, order.Id
            select order.Id).ToArrayAsync(cancellationToken);
        return ids.Select(OrderId.From).ToArray();
    }

    public async Task<PaperOrderState?> GetAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var state = await (
            from order in context.Orders.AsNoTracking()
            join reservation in context.SpotOrderReservations.AsNoTracking()
                on order.Id equals reservation.OrderId
            where order.Id == orderId.Value
            select new
            {
                order.Id,
                order.Exchange,
                order.Symbol,
                order.Side,
                order.Type,
                order.Status,
                order.ApprovedQuantity,
                order.FilledQuantity,
                order.LimitPrice,
                reservation.QuoteAsset,
                ReservationStatus = reservation.Status,
                reservation.CreatedAt
            }).SingleOrDefaultAsync(cancellationToken);
        return state is null
            ? null
            : new PaperOrderState(
                OrderId.From(state.Id),
                InstrumentId.Create(state.Exchange, state.Symbol),
                AssetCode.Create(state.QuoteAsset),
                (OrderSide)state.Side,
                (OrderType)state.Type,
                (OrderStatus)state.Status,
                state.ApprovedQuantity,
                state.FilledQuantity,
                state.LimitPrice is null ? null : Price.From(state.LimitPrice.Value),
                (SpotReservationStatus)state.ReservationStatus,
                state.CreatedAt);
    }
}
