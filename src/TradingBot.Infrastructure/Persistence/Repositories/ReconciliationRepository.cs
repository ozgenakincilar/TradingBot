using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class ReconciliationRepository(TradingBotDbContext context) : IReconciliationRepository
{
    public async Task<ReconciliationRunRecord?> GetRunAsync(
        string exchange,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        var entity = await context.ReconciliationRuns.AsNoTracking().SingleOrDefaultAsync(
            run => run.Exchange == exchange && run.SnapshotId == snapshotId,
            cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<TradingSafetyStateRecord?> GetSafetyStateAsync(
        string exchange,
        CancellationToken cancellationToken)
    {
        var entity = await context.TradingSafetyStates.FindAsync([exchange], cancellationToken);
        return entity is null
            ? null
            : new TradingSafetyStateRecord(
                entity.Exchange,
                entity.IsHalted,
                entity.HaltReason,
                entity.UpdatedAt);
    }

    public async Task<IReadOnlyCollection<ReconciliationRunRecord>> GetRecentRunsAsync(
        string exchange,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var entities = await context.ReconciliationRuns
            .AsNoTracking()
            .Where(run => run.Exchange == exchange)
            .OrderByDescending(static run => run.SnapshotOccurredAt)
            .ThenByDescending(static run => run.SnapshotId)
            .Take(count)
            .ToArrayAsync(cancellationToken);
        return entities.Select(Map).ToArray();
    }

    public async Task<TradingSafetyRecoveryRecord?> GetRecoveryAsync(
        Guid recoveryId,
        CancellationToken cancellationToken)
    {
        var entity = await context.TradingSafetyRecoveries.FindAsync([recoveryId], cancellationToken);
        return entity is null
            ? null
            : new TradingSafetyRecoveryRecord(
                entity.Id,
                entity.Exchange,
                entity.OperatorId,
                entity.Reason,
                entity.OccurredAt,
                entity.EvidenceSnapshotIdsJson,
                entity.CorrelationId);
    }

    public Task<bool> IsTradingHaltedAsync(string exchange, CancellationToken cancellationToken) =>
        context.TradingSafetyStates.AsNoTracking().AnyAsync(
            state => state.Exchange == exchange && state.IsHalted,
            cancellationToken);

    public void AddRun(ReconciliationRunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        context.ReconciliationRuns.Add(new ReconciliationRunEntity
        {
            Exchange = run.Exchange,
            SnapshotId = run.SnapshotId,
            SnapshotHash = run.SnapshotHash,
            SnapshotOccurredAt = run.SnapshotOccurredAt,
            CanTrade = run.CanTrade,
            IsConsistent = run.IsConsistent,
            DiscrepancyCount = run.DiscrepancyCount,
            DiscrepanciesJson = run.DiscrepanciesJson,
            CorrelationId = run.CorrelationId
        });
    }

    public void StoreSafetyState(TradingSafetyStateRecord state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var entity = context.TradingSafetyStates.Local.SingleOrDefault(
            candidate => candidate.Exchange == state.Exchange);
        if (entity is null)
        {
            entity = new TradingSafetyStateEntity { Exchange = state.Exchange };
            context.TradingSafetyStates.Add(entity);
        }

        entity.IsHalted = state.IsHalted;
        entity.HaltReason = state.HaltReason;
        entity.UpdatedAt = state.UpdatedAt;
    }

    public void AddRecovery(TradingSafetyRecoveryRecord recovery)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        context.TradingSafetyRecoveries.Add(new TradingSafetyRecoveryEntity
        {
            Id = recovery.Id,
            Exchange = recovery.Exchange,
            OperatorId = recovery.OperatorId,
            Reason = recovery.Reason,
            OccurredAt = recovery.OccurredAt,
            EvidenceSnapshotIdsJson = recovery.EvidenceSnapshotIdsJson,
            CorrelationId = recovery.CorrelationId
        });
    }

    private static ReconciliationRunRecord Map(ReconciliationRunEntity entity) =>
        new(
            entity.Exchange,
            entity.SnapshotId,
            entity.SnapshotHash,
            entity.SnapshotOccurredAt,
            entity.CanTrade,
            entity.IsConsistent,
            entity.DiscrepancyCount,
            entity.DiscrepanciesJson,
            entity.CorrelationId);
}
