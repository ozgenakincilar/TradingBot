namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class ReconciliationRunEntity
{
    public required string Exchange { get; set; }

    public required string SnapshotId { get; set; }

    public required string SnapshotHash { get; set; }

    public DateTimeOffset SnapshotOccurredAt { get; set; }

    public bool CanTrade { get; set; }

    public bool IsConsistent { get; set; }

    public int DiscrepancyCount { get; set; }

    public required string DiscrepanciesJson { get; set; }

    public required string CorrelationId { get; set; }
}
