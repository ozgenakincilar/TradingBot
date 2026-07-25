using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;

namespace TradingBot.Application.Abstractions.Persistence;

public interface IRiskDecisionRepository
{
    void Add(Guid id, OrderId orderId, RiskDecision decision, DateTimeOffset occurredAt);
}
