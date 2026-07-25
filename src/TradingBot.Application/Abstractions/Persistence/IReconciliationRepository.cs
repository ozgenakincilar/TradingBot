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

public interface IReconciliationRepository
{
    Task<ReconciliationRunRecord?> GetRunAsync(
        string exchange,
        string snapshotId,
        CancellationToken cancellationToken);

    Task<TradingSafetyStateRecord?> GetSafetyStateAsync(
        string exchange,
        CancellationToken cancellationToken);

    Task<bool> IsTradingHaltedAsync(string exchange, CancellationToken cancellationToken);

    void AddRun(ReconciliationRunRecord run);

    void StoreSafetyState(TradingSafetyStateRecord state);
}
