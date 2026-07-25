using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;

namespace TradingBot.Application.Reconciliation;

public sealed record RecoverTradingSafetyCommand(
    Guid RecoveryId,
    string Exchange,
    string OperatorId,
    string Reason,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public enum TradingSafetyRecoveryResult
{
    Recovered = 1,
    AlreadyRecovered = 2,
    AlreadySafe = 3
}

public sealed class RecoverTradingSafety(
    IReconciliationRepository reconciliation,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private const int RequiredCleanSnapshots = 2;
    private const string IntegrationEventType = "operations.trading-safety-recovered.v1";

    public async Task<TradingSafetyRecoveryResult> HandleAsync(
        RecoverTradingSafetyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);
        var result = TradingSafetyRecoveryResult.Recovered;

        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var existing = await reconciliation.GetRecoveryAsync(
                command.RecoveryId,
                transactionCancellationToken);
            if (existing is not null)
            {
                if (existing.Exchange != command.Exchange ||
                    existing.OperatorId != command.OperatorId ||
                    existing.Reason != command.Reason ||
                    existing.OccurredAt != command.OccurredAt ||
                    existing.CorrelationId != command.CorrelationId)
                {
                    throw new DomainRuleViolationException(
                        "Recovery id conflicts with a different operator action.");
                }

                result = TradingSafetyRecoveryResult.AlreadyRecovered;
                return;
            }

            var safety = await reconciliation.GetSafetyStateAsync(
                command.Exchange,
                transactionCancellationToken);
            if (safety is null || !safety.IsHalted)
            {
                result = TradingSafetyRecoveryResult.AlreadySafe;
                return;
            }

            if (command.OccurredAt < safety.UpdatedAt)
            {
                throw new DomainRuleViolationException("Recovery cannot move safety state backwards in time.");
            }

            var evidence = await reconciliation.GetRecentRunsAsync(
                command.Exchange,
                RequiredCleanSnapshots,
                transactionCancellationToken);
            if (evidence.Count != RequiredCleanSnapshots ||
                evidence.Any(run =>
                    !run.IsConsistent ||
                    !run.CanTrade ||
                    run.DiscrepancyCount != 0 ||
                    run.SnapshotOccurredAt <= safety.UpdatedAt))
            {
                throw new DomainRuleViolationException(
                    $"Recovery requires {RequiredCleanSnapshots} consecutive clean snapshots after the halt.");
            }

            var snapshotIds = evidence
                .OrderBy(static run => run.SnapshotOccurredAt)
                .Select(static run => run.SnapshotId)
                .ToArray();
            var evidenceJson = JsonSerializer.Serialize(snapshotIds);
            reconciliation.StoreSafetyState(new TradingSafetyStateRecord(
                command.Exchange,
                false,
                null,
                command.OccurredAt));
            reconciliation.AddRecovery(new TradingSafetyRecoveryRecord(
                command.RecoveryId,
                command.Exchange,
                command.OperatorId,
                command.Reason,
                command.OccurredAt,
                evidenceJson,
                command.CorrelationId));

            var payload = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                command.RecoveryId,
                command.Exchange,
                command.OperatorId,
                command.Reason,
                command.OccurredAt,
                EvidenceSnapshotIds = snapshotIds
            });
            audit.Add(new AuditRecord(
                idGenerator.NewGuid(),
                command.OccurredAt,
                "Operations",
                "TradingSafetyRecovered",
                "ExchangeAccount",
                command.Exchange,
                command.CorrelationId,
                payload));
            outbox.Add(new OutboxRecord(
                idGenerator.NewGuid(),
                command.OccurredAt,
                IntegrationEventType,
                command.CorrelationId,
                payload));
        }, cancellationToken);

        return result;
    }

    private static void Validate(RecoverTradingSafetyCommand command)
    {
        if (command.RecoveryId == Guid.Empty)
        {
            throw new ArgumentException("Recovery id is required.", nameof(command));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.Exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.OperatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (command.Exchange.Length > 32 || command.OperatorId.Length > 64 ||
            command.Reason.Length > 512 || command.CorrelationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
