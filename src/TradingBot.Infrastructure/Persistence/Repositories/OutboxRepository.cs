using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository(TradingBotDbContext context) : IOutboxRepository
{
    public void Add(OutboxRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        context.OutboxMessages.Add(new OutboxMessageEntity
        {
            Id = record.Id,
            OccurredAt = record.OccurredAt,
            MessageType = record.MessageType,
            CorrelationId = record.CorrelationId,
            Payload = record.Payload
        });
    }
}
