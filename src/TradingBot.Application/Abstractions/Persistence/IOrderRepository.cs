using TradingBot.Domain.Orders;

namespace TradingBot.Application.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<bool> ExistsAsync(ClientOrderId clientOrderId, CancellationToken cancellationToken);

    void Add(Order order);
}
