using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Orders;
using TradingBot.Application.Reconciliation;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Reconciliation;
using TradingBot.Domain.Risk;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;
using TradingBot.Infrastructure.Persistence.Repositories;

namespace TradingBot.Infrastructure.Tests;

public sealed class SpotReconciliationIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";

    [Fact]
    public async Task HaltRequiresTwoCleanSnapshotsAndOperatorRecoveryBeforeNewOrder()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid().ToString("N");
        var exchange = $"C{unique[..12]}".ToUpperInvariant();
        var snapshotId = $"snapshot-{unique}";
        var correlations = new[]
        {
            $"reconcile-{unique}",
            $"clean-a-{unique}",
            $"clean-b-{unique}",
            $"recovery-{unique}",
            $"order-{unique}"
        };
        var occurredAt = DateTimeOffset.UtcNow;
        var staleOrder = CreateRiskApprovedOrder(exchange, $"OLD-{unique}", occurredAt.AddSeconds(1));
        var newOrder = CreateRiskApprovedOrder(exchange, $"NEW-{unique}", occurredAt.AddSeconds(5));

        try
        {
            await using (var seed = CreateContext(connectionString))
            {
                seed.AssetBalances.Add(new AssetBalanceEntity
                {
                    Exchange = exchange,
                    Asset = "USDT",
                    Total = 100m,
                    Reserved = 0m,
                    UpdatedAt = occurredAt.AddSeconds(-1)
                });
                await seed.SaveChangesAsync();
            }

            var command = new ReconcileSpotAccountCommand(
                new SpotAccountSnapshot(
                    exchange,
                    snapshotId,
                    true,
                    occurredAt,
                    [new ReconciliationBalance(AssetCode.Create("USDT"), 99m, 0m)],
                    []),
                0m,
                correlations[0]);
            await using (var context = CreateContext(connectionString))
            {
                var handler = CreateReconciliationHandler(context);
                var outcome = await handler.HandleAsync(command, CancellationToken.None);
                Assert.False(outcome.IsConsistent);
                Assert.True(outcome.IsTradingHalted);
            }

            await using (var duplicateContext = CreateContext(connectionString))
            {
                var duplicate = await CreateReconciliationHandler(duplicateContext).HandleAsync(
                    command,
                    CancellationToken.None);
                Assert.Equal(ReconciliationProcessingStatus.AlreadyProcessed, duplicate.Status);
            }

            await using (var orderContext = CreateContext(connectionString))
            {
                var action = () => CreateOrderHandler(orderContext).HandleAsync(
                    new PersistRiskApprovedOrderCommand(
                        staleOrder,
                        RiskDecision.Approve(staleOrder.ApprovedQuantity),
                        staleOrder.UpdatedAt,
                        $"blocked-{unique}"),
                    CancellationToken.None);
                await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(action);
            }

            await ReconcileCleanAsync(
                connectionString,
                exchange,
                $"clean-a-{unique}",
                occurredAt.AddSeconds(2),
                correlations[1]);
            await ReconcileCleanAsync(
                connectionString,
                exchange,
                $"clean-b-{unique}",
                occurredAt.AddSeconds(3),
                correlations[2]);

            var recoveryId = Guid.NewGuid();
            await using (var recoveryContext = CreateContext(connectionString))
            {
                var recovery = await CreateRecoveryHandler(recoveryContext).HandleAsync(
                    new RecoverTradingSafetyCommand(
                        recoveryId,
                        exchange,
                        "integration-operator",
                        "Reviewed two clean account snapshots.",
                        occurredAt.AddSeconds(4),
                        correlations[3]),
                    CancellationToken.None);
                Assert.Equal(TradingSafetyRecoveryResult.Recovered, recovery);
            }

            await using (var staleContext = CreateContext(connectionString))
            {
                var staleAction = () => CreateOrderHandler(staleContext).HandleAsync(
                    new PersistRiskApprovedOrderCommand(
                        staleOrder,
                        RiskDecision.Approve(staleOrder.ApprovedQuantity),
                        staleOrder.UpdatedAt,
                        $"stale-{unique}"),
                    CancellationToken.None);
                await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(staleAction);
            }

            await using (var newOrderContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    PersistOrderResult.Stored,
                    await CreateOrderHandler(newOrderContext).HandleAsync(
                        new PersistRiskApprovedOrderCommand(
                            newOrder,
                            RiskDecision.Approve(newOrder.ApprovedQuantity),
                            newOrder.UpdatedAt,
                            correlations[4]),
                        CancellationToken.None));
            }

            await using var verification = CreateContext(connectionString);
            Assert.False((await verification.TradingSafetyStates.SingleAsync(x => x.Exchange == exchange)).IsHalted);
            Assert.Equal(3, await verification.ReconciliationRuns.CountAsync(x => x.Exchange == exchange));
            Assert.Equal(1, await verification.TradingSafetyRecoveries.CountAsync(x => x.Id == recoveryId));
            Assert.False(await verification.Orders.AnyAsync(x => x.Id == staleOrder.Id.Value));
            Assert.True(await verification.Orders.AnyAsync(x => x.Id == newOrder.Id.Value));
        }
        finally
        {
            await CleanupAsync(connectionString, exchange, correlations);
        }
    }

    private static Order CreateRiskApprovedOrder(
        string exchange,
        string unique,
        DateTimeOffset occurredAt)
    {
        var order = Order.Create(
            OrderId.New(),
            ClientOrderId.Create($"TEST-REC-{unique}"),
            InstrumentId.Create(exchange, "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(1m),
            Price.From(10m),
            occurredAt);
        order.ApproveRisk(Quantity.From(1m), occurredAt);
        return order;
    }

    private static TradingBotDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TradingBotDbContext>()
            .UseSqlServer(
                connectionString,
                static sqlServer => sqlServer.EnableRetryOnFailure(maxRetryCount: 3))
            .Options;
        return new TradingBotDbContext(options);
    }

    private static ReconcileSpotAccount CreateReconciliationHandler(TradingBotDbContext context) =>
        new(
            new OrderRepository(context),
            new PortfolioRepository(context),
            new ReconciliationRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static PersistRiskApprovedOrder CreateOrderHandler(TradingBotDbContext context) =>
        new(
            new OrderRepository(context),
            new RiskDecisionRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new ReconciliationRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static RecoverTradingSafety CreateRecoveryHandler(TradingBotDbContext context) =>
        new(
            new ReconciliationRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static async Task ReconcileCleanAsync(
        string connectionString,
        string exchange,
        string snapshotId,
        DateTimeOffset occurredAt,
        string correlationId)
    {
        await using var context = CreateContext(connectionString);
        await CreateReconciliationHandler(context).HandleAsync(
            new ReconcileSpotAccountCommand(
                new SpotAccountSnapshot(
                    exchange,
                    snapshotId,
                    true,
                    occurredAt,
                    [new ReconciliationBalance(AssetCode.Create("USDT"), 100m, 0m)],
                    []),
                0m,
                correlationId),
            CancellationToken.None);
    }

    private static async Task CleanupAsync(
        string connectionString,
        string exchange,
        IReadOnlyCollection<string> correlations)
    {
        await using var cleanup = CreateContext(connectionString);
        await cleanup.AuditEvents.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        await cleanup.OutboxMessages.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        var orderIds = await cleanup.Orders
            .Where(x => x.Exchange == exchange)
            .Select(x => x.Id)
            .ToArrayAsync();
        await cleanup.RiskDecisions.Where(x => orderIds.Contains(x.OrderId)).ExecuteDeleteAsync();
        await cleanup.TradingSafetyRecoveries.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.ReconciliationRuns.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.TradingSafetyStates.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.AssetBalances.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.Orders.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
    }
}
