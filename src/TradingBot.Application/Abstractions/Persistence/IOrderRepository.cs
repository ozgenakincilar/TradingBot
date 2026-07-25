using TradingBot.Domain.Orders;

namespace TradingBot.Application.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<bool> ExistsAsync(ClientOrderId clientOrderId, CancellationToken cancellationToken);

    Task<Order?> GetAsync(OrderId orderId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Order>> GetActiveAsync(
        string exchange,
        CancellationToken cancellationToken);

    void Add(Order order);

    void Store(Order order);
}
