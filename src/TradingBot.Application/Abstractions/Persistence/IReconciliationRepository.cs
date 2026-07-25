namespace TradingBot.Application.Abstractions.Persistence;

public sealed record ReconciliationRunRecord(
    string Exchange,
    string SnapshotId,
    string SnapshotHash,
    DateTimeOffset SnapshotOccurredAt,
    bool CanTrade,
    bool IsConsistent,
    int DiscrepancyCount,
    string DiscrepanciesJson,
    string CorrelationId);

public sealed record TradingSafetyStateRecord(
    string Exchange,
    bool IsHalted,
    string? HaltReason,
    DateTimeOffset UpdatedAt);

public sealed record TradingSafetyRecoveryRecord(
    Guid Id,
    string Exchange,
    string OperatorId,
    string Reason,
    DateTimeOffset OccurredAt,
    string EvidenceSnapshotIdsJson,
    string CorrelationId);

public interface IReconciliationRepository
{
    Task<ReconciliationRunRecord?> GetRunAsync(
        string exchange,
        string snapshotId,
        CancellationToken cancellationToken);

    Task<TradingSafetyStateRecord?> GetSafetyStateAsync(
        string exchange,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ReconciliationRunRecord>> GetRecentRunsAsync(
        string exchange,
        int count,
        CancellationToken cancellationToken);

    Task<TradingSafetyRecoveryRecord?> GetRecoveryAsync(
        Guid recoveryId,
        CancellationToken cancellationToken);

    Task<bool> IsTradingHaltedAsync(string exchange, CancellationToken cancellationToken);

    void AddRun(ReconciliationRunRecord run);

    void StoreSafetyState(TradingSafetyStateRecord state);

    void AddRecovery(TradingSafetyRecoveryRecord recovery);
}
