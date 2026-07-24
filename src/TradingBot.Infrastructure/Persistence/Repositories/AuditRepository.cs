using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository(TradingBotDbContext context) : IAuditRepository
{
    public void Add(AuditRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        context.AuditEvents.Add(new AuditEventEntity
        {
            Id = record.Id,
            OccurredAt = record.OccurredAt,
            Category = record.Category,
            Action = record.Action,
            AggregateType = record.AggregateType,
            AggregateId = record.AggregateId,
            CorrelationId = record.CorrelationId,
            Data = record.Data
        });
    }
}
