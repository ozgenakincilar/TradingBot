namespace TradingBot.Application.Abstractions.Persistence;

public sealed record OutboxRecord(
    Guid Id,
    DateTimeOffset OccurredAt,
    string MessageType,
    string CorrelationId,
    string Payload);

public interface IOutboxRepository
{
    void Add(OutboxRecord record);
}
