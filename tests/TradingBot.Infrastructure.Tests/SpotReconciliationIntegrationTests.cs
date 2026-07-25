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
    public async Task BalanceMismatchAtomicallyPersistsHaltAndBlocksNewOrder()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid().ToString("N");
        var exchange = $"C{unique[..12]}".ToUpperInvariant();
        var snapshotId = $"snapshot-{unique}";
        var correlationId = $"reconcile-{unique}";
        var occurredAt = DateTimeOffset.UtcNow;
        var order = CreateRiskApprovedOrder(exchange, unique, occurredAt.AddSeconds(1));

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
                correlationId);
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
                        order,
                        RiskDecision.Approve(order.ApprovedQuantity),
                        order.UpdatedAt,
                        $"order-{unique}"),
                    CancellationToken.None);
                await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(action);
            }

            await using var verification = CreateContext(connectionString);
            Assert.True((await verification.TradingSafetyStates.SingleAsync(x => x.Exchange == exchange)).IsHalted);
            Assert.Equal(1, await verification.ReconciliationRuns.CountAsync(x => x.Exchange == exchange));
            Assert.Equal(1, await verification.AuditEvents.CountAsync(x => x.CorrelationId == correlationId));
            Assert.Equal(1, await verification.OutboxMessages.CountAsync(x => x.CorrelationId == correlationId));
            Assert.False(await verification.Orders.AnyAsync(x => x.Id == order.Id.Value));
        }
        finally
        {
            await CleanupAsync(connectionString, exchange, correlationId);
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

    private static async Task CleanupAsync(
        string connectionString,
        string exchange,
        string correlationId)
    {
        await using var cleanup = CreateContext(connectionString);
        await cleanup.AuditEvents.Where(x => x.CorrelationId == correlationId).ExecuteDeleteAsync();
        await cleanup.OutboxMessages.Where(x => x.CorrelationId == correlationId).ExecuteDeleteAsync();
        await cleanup.ReconciliationRuns.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.TradingSafetyStates.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.AssetBalances.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
    }
}
