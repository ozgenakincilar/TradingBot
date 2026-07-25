namespace TradingBot.Application.Abstractions.Persistence;

public sealed record AuditRecord(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Category,
    string Action,
    string AggregateType,
    string AggregateId,
    string CorrelationId,
    string Data);

public interface IAuditRepository
{
    void Add(AuditRecord record);
}
