namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class OutboxMessageEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string MessageType { get; set; }

    public required string Payload { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
