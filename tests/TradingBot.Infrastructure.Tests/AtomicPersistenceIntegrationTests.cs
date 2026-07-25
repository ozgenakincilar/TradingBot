using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Orders;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Repositories;

namespace TradingBot.Infrastructure.Tests;

public sealed class AtomicPersistenceIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";

    [Fact]
    public async Task PersistRiskApprovedOrder_CommitsAllAtomicRecordsToSqlServer()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid();
        var correlationId = $"test-{unique:N}";
        var clientOrderId = ClientOrderId.Create($"TEST-{unique:N}");
        var order = CreateOrder(OrderId.New(), clientOrderId);

        await using var context = CreateContext(connectionString);
        var handler = CreateHandler(context);

        try
        {
            var result = await handler.HandleAsync(
                new PersistRiskApprovedOrderCommand(
                    order,
                    RiskDecision.Approve(order.ApprovedQuantity),
                    order.UpdatedAt,
                    correlationId),
                CancellationToken.None);

            Assert.Equal(PersistOrderResult.Stored, result);

            await using var verification = CreateContext(connectionString);
            Assert.True(await verification.Orders.AnyAsync(x => x.Id == order.Id.Value));
            Assert.True(await verification.RiskDecisions.AnyAsync(x => x.OrderId == order.Id.Value));
            Assert.True(await verification.AuditEvents.AnyAsync(x => x.CorrelationId == correlationId));
            Assert.True(await verification.OutboxMessages.AnyAsync(x => x.CorrelationId == correlationId));
        }
        finally
        {
            await CleanupAsync(connectionString, order.Id.Value, correlationId);
        }
    }

    [Fact]
    public async Task PersistRiskApprovedOrder_ConcurrentDuplicateCreatesOneEconomicOrder()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid();
        var clientOrderId = ClientOrderId.Create($"TEST-DUP-{unique:N}");
        var firstOrder = CreateOrder(OrderId.New(), clientOrderId);
        var secondOrder = CreateOrder(OrderId.New(), clientOrderId);
        var firstCorrelation = $"test-a-{unique:N}";
        var secondCorrelation = $"test-b-{unique:N}";

        await using var firstContext = CreateContext(connectionString);
        await using var secondContext = CreateContext(connectionString);

        try
        {
            var results = await Task.WhenAll(
                CreateHandler(firstContext).HandleAsync(
                    new PersistRiskApprovedOrderCommand(
                        firstOrder,
                        RiskDecision.Approve(firstOrder.ApprovedQuantity),
                        firstOrder.UpdatedAt,
                        firstCorrelation),
                    CancellationToken.None),
                CreateHandler(secondContext).HandleAsync(
                    new PersistRiskApprovedOrderCommand(
                        secondOrder,
                        RiskDecision.Approve(secondOrder.ApprovedQuantity),
                        secondOrder.UpdatedAt,
                        secondCorrelation),
                    CancellationToken.None));

            Assert.Contains(PersistOrderResult.Stored, results);
            Assert.Contains(PersistOrderResult.AlreadyExists, results);

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                1,
                await verification.Orders.CountAsync(x => x.ClientOrderId == clientOrderId.Value));
        }
        finally
        {
            await CleanupAsync(connectionString, firstOrder.Id.Value, firstCorrelation);
            await CleanupAsync(connectionString, secondOrder.Id.Value, secondCorrelation);
        }
    }

    private static Order CreateOrder(OrderId id, ClientOrderId clientOrderId)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            id,
            clientOrderId,
            InstrumentId.Create("INTEGRATION", "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(0.01m),
            Price.From(100_000m),
            now);
        order.ApproveRisk(Quantity.From(0.01m), now);
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

    private static PersistRiskApprovedOrder CreateHandler(TradingBotDbContext context) =>
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
        Guid orderId,
        string correlationId)
    {
        await using var cleanup = CreateContext(connectionString);
        await cleanup.RiskDecisions.Where(x => x.OrderId == orderId).ExecuteDeleteAsync();
        await cleanup.AuditEvents.Where(x => x.CorrelationId == correlationId).ExecuteDeleteAsync();
        await cleanup.OutboxMessages.Where(x => x.CorrelationId == correlationId).ExecuteDeleteAsync();
        await cleanup.Orders.Where(x => x.Id == orderId).ExecuteDeleteAsync();
    }
}
