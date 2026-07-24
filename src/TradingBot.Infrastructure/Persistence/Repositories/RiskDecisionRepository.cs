using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class RiskDecisionRepository(TradingBotDbContext context) : IRiskDecisionRepository
{
    public void Add(Guid id, OrderId orderId, RiskDecision decision, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(decision);

        context.RiskDecisions.Add(new RiskDecisionEntity
        {
            Id = id,
            OrderId = orderId.Value,
            DecisionType = (byte)decision.Type,
            ApprovedQuantity = decision.ApprovedQuantity?.Value,
            RejectionCode = (int)decision.RejectionCode,
            Reason = decision.Reason,
            OccurredAt = occurredAt
        });
    }
}
