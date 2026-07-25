namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class AuditEventEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string Category { get; set; }

    public required string Action { get; set; }

    public required string AggregateType { get; set; }

    public required string AggregateId { get; set; }

    public string? CorrelationId { get; set; }

    public required string Data { get; set; }
}
