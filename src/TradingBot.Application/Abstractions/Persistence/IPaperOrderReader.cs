using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Abstractions.Persistence;

public sealed record PaperOrderState(
    OrderId OrderId,
    InstrumentId InstrumentId,
    AssetCode QuoteAsset,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal ApprovedQuantity,
    decimal FilledQuantity,
    Price? LimitPrice,
    SpotReservationStatus ReservationStatus,
    DateTimeOffset ReservationCreatedAt);

public interface IPaperOrderReader
{
    Task<PaperOrderState?> GetAsync(OrderId orderId, CancellationToken cancellationToken);
}
