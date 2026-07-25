namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class TradingSafetyRecoveryEntity
{
    public Guid Id { get; set; }

    public required string Exchange { get; set; }

    public required string OperatorId { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string EvidenceSnapshotIdsJson { get; set; }

    public required string CorrelationId { get; set; }
}
