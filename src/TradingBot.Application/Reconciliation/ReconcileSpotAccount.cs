using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Reconciliation;

namespace TradingBot.Application.Reconciliation;

public sealed record ReconcileSpotAccountCommand(
    SpotAccountSnapshot Snapshot,
    decimal BalanceTolerance,
    string CorrelationId);

public enum ReconciliationProcessingStatus
{
    Processed = 1,
    AlreadyProcessed = 2
}

public sealed record ReconcileSpotAccountOutcome(
    ReconciliationProcessingStatus Status,
    bool IsConsistent,
    bool IsTradingHalted,
    int DiscrepancyCount);

public sealed class ReconcileSpotAccount(
    IOrderRepository orders,
    IPortfolioRepository portfolio,
    IReconciliationRepository reconciliation,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private const string IntegrationEventType = "operations.spot-account-reconciled.v1";
    private readonly SpotReconciliationEngine _engine = new();

    public async Task<ReconcileSpotAccountOutcome> HandleAsync(
        ReconcileSpotAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);
        var snapshotHash = CalculateSnapshotHash(command.Snapshot);
        ReconcileSpotAccountOutcome? outcome = null;

        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var existing = await reconciliation.GetRunAsync(
                command.Snapshot.Exchange,
                command.Snapshot.SnapshotId,
                transactionCancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.SnapshotHash, snapshotHash, StringComparison.Ordinal))
                {
                    throw new DomainRuleViolationException(
                        "Snapshot id conflicts with different reconciliation content.");
                }

                var existingSafety = await reconciliation.GetSafetyStateAsync(
                    command.Snapshot.Exchange,
                    transactionCancellationToken);
                outcome = new ReconcileSpotAccountOutcome(
                    ReconciliationProcessingStatus.AlreadyProcessed,
                    existing.IsConsistent,
                    existingSafety?.IsHalted ?? false,
                    existing.DiscrepancyCount);
                return;
            }

            var safety = await reconciliation.GetSafetyStateAsync(
                command.Snapshot.Exchange,
                transactionCancellationToken);
            if (safety is not null && command.Snapshot.OccurredAt < safety.UpdatedAt)
            {
                throw new DomainRuleViolationException(
                    "A reconciliation snapshot cannot move account safety state backwards in time.");
            }

            var localBalances = await portfolio.GetBalancesAsync(
                command.Snapshot.Exchange,
                transactionCancellationToken);
            var localOrders = await orders.GetActiveAsync(
                command.Snapshot.Exchange,
                transactionCancellationToken);
            var localState = new LocalSpotAccountState(
                command.Snapshot.Exchange,
                localBalances.Select(static balance => new ReconciliationBalance(
                    balance.Asset,
                    balance.Total,
                    balance.Reserved)).ToArray(),
                localOrders.Select(static order => new ReconciliationOrder(
                    order.ClientOrderId,
                    order.ExchangeOrderId,
                    order.InstrumentId,
                    order.Side,
                    order.FilledQuantity)).ToArray());
            var result = _engine.Compare(command.Snapshot, localState, command.BalanceTolerance);
            var discrepanciesJson = JsonSerializer.Serialize(result.Discrepancies);

            var isHalted = safety?.IsHalted == true || result.ShouldHaltTrading;
            var haltReason = safety?.IsHalted == true
                ? safety.HaltReason
                : result.ShouldHaltTrading
                    ? $"Reconciliation {command.Snapshot.SnapshotId} found {result.Discrepancies.Count} discrepancy(s)."
                    : null;
            reconciliation.StoreSafetyState(new TradingSafetyStateRecord(
                command.Snapshot.Exchange,
                isHalted,
                haltReason,
                command.Snapshot.OccurredAt));
            reconciliation.AddRun(new ReconciliationRunRecord(
                command.Snapshot.Exchange,
                command.Snapshot.SnapshotId,
                snapshotHash,
                command.Snapshot.OccurredAt,
                command.Snapshot.CanTrade,
                result.IsConsistent,
                result.Discrepancies.Count,
                discrepanciesJson,
                command.CorrelationId));

            var payload = JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                command.Snapshot.Exchange,
                command.Snapshot.SnapshotId,
                command.Snapshot.OccurredAt,
                result.IsConsistent,
                IsTradingHalted = isHalted,
                Discrepancies = result.Discrepancies
            });
            audit.Add(new AuditRecord(
                idGenerator.NewGuid(),
                command.Snapshot.OccurredAt,
                "Operations",
                "SpotAccountReconciled",
                "ExchangeAccount",
                command.Snapshot.Exchange,
                command.CorrelationId,
                payload));
            outbox.Add(new OutboxRecord(
                idGenerator.NewGuid(),
                command.Snapshot.OccurredAt,
                IntegrationEventType,
                command.CorrelationId,
                payload));

            outcome = new ReconcileSpotAccountOutcome(
                ReconciliationProcessingStatus.Processed,
                result.IsConsistent,
                isHalted,
                result.Discrepancies.Count);
        }, cancellationToken);

        return outcome ?? throw new InvalidOperationException("Reconciliation did not produce an outcome.");
    }

    private static string CalculateSnapshotHash(SpotAccountSnapshot snapshot)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            snapshot.Exchange,
            snapshot.SnapshotId,
            snapshot.CanTrade,
            snapshot.OccurredAt,
            Balances = snapshot.Balances
                .OrderBy(static balance => balance.Asset.Value)
                .Select(static balance => new { Asset = balance.Asset.Value, balance.Total, balance.Reserved }),
            Orders = snapshot.OpenOrders
                .OrderBy(static order => order.ClientOrderId.Value)
                .Select(static order => new
                {
                    ClientOrderId = order.ClientOrderId.Value,
                    order.ExchangeOrderId,
                    order.InstrumentId.Exchange,
                    order.InstrumentId.Symbol,
                    order.Side,
                    order.FilledQuantity
                })
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static void Validate(ReconcileSpotAccountCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Snapshot.Exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Snapshot.SnapshotId);
        if (command.CorrelationId.Length > 64 || command.Snapshot.Exchange.Length > 32 ||
            command.Snapshot.SnapshotId.Length > 128 || command.BalanceTolerance < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
