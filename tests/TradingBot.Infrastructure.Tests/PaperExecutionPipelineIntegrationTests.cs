using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Execution;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;
using TradingBot.Infrastructure.Persistence.Repositories;

namespace TradingBot.Infrastructure.Tests;

public sealed class PaperExecutionPipelineIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";

    [Fact]
    public async Task MarketEventPersistsOneAtomicPartialFillToSqlServer()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid().ToString("N");
        var exchange = $"P{unique[..12]}".ToUpperInvariant();
        var order = CreateRiskApprovedOrder(exchange, unique);
        var correlations = new[] { $"reserve-{unique}", $"paper-{unique}" };

        try
        {
            await SeedAsync(connectionString, order);
            await using (var reserveContext = CreateContext(connectionString))
            {
                await CreateReserveHandler(reserveContext).HandleAsync(
                    new ReserveSpotOrderCommand(
                        order.Id,
                        AssetCode.Create("BTC"),
                        AssetCode.Create("USDT"),
                        Price.From(100m),
                        Money.Create(1m, "USDT"),
                        order.UpdatedAt.AddSeconds(1),
                        correlations[0]),
                    CancellationToken.None);
            }

            await MarkOpenAsync(connectionString, order.Id, order.UpdatedAt.AddSeconds(2));
            var command = new ProcessPaperOrderSnapshotCommand(
                order.Id,
                $"market-{unique}",
                new PaperTopOfBookSnapshot(
                    order.InstrumentId,
                    Price.From(89m),
                    2m,
                    Price.From(90m),
                    2m,
                    order.UpdatedAt.AddSeconds(3)),
                new PaperExecutionPolicy(
                    TimeSpan.FromMilliseconds(100),
                    Percentage.FromPercent(0.1m),
                    10m,
                    Percentage.FromPercent(25m)),
                correlations[1]);

            await using (var processContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    PaperOrderProcessingStatus.FillApplied,
                    (await CreateProcessHandler(processContext).HandleAsync(
                        command,
                        CancellationToken.None)).Status);
            }

            await using (var duplicateContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    PaperOrderProcessingStatus.FillAlreadyApplied,
                    (await CreateProcessHandler(duplicateContext).HandleAsync(
                        command,
                        CancellationToken.None)).Status);
            }

            await using var verification = CreateContext(connectionString);
            var storedOrder = await verification.Orders.SingleAsync(x => x.Id == order.Id.Value);
            var quote = await verification.AssetBalances.SingleAsync(
                x => x.Exchange == exchange && x.Asset == "USDT");
            var baseBalance = await verification.AssetBalances.SingleAsync(
                x => x.Exchange == exchange && x.Asset == "BTC");
            Assert.Equal((byte)OrderStatus.PartiallyFilled, storedOrder.Status);
            Assert.Equal(0.5m, storedOrder.FilledQuantity);
            Assert.Equal(954.909955m, quote.Total);
            Assert.Equal(0.5m, baseBalance.Total);
            Assert.Equal(1, await verification.SpotExecutions.CountAsync(x => x.OrderId == order.Id.Value));
        }
        finally
        {
            await CleanupAsync(connectionString, order.Id.Value, exchange, correlations);
        }
    }

    private static Order CreateRiskApprovedOrder(string exchange, string unique)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            OrderId.New(),
            ClientOrderId.Create($"PAPER-PIPE-{unique}"),
            InstrumentId.Create(exchange, "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Market,
            Quantity.From(2m),
            null,
            now);
        order.ApproveRisk(Quantity.From(2m), now);
        return order;
    }

    private static async Task SeedAsync(string connectionString, Order order)
    {
        await using var context = CreateContext(connectionString);
        new OrderRepository(context).Add(order);
        context.AssetBalances.Add(new AssetBalanceEntity
        {
            Exchange = order.InstrumentId.Exchange,
            Asset = "USDT",
            Total = 1_000m,
            Reserved = 0m,
            UpdatedAt = order.UpdatedAt
        });
        await context.SaveChangesAsync();
    }

    private static async Task MarkOpenAsync(
        string connectionString,
        OrderId orderId,
        DateTimeOffset occurredAt)
    {
        await using var context = CreateContext(connectionString);
        var repository = new OrderRepository(context);
        var order = await repository.GetAsync(orderId, CancellationToken.None)
            ?? throw new InvalidOperationException("Order was not found.");
        order.MarkSubmitting(occurredAt);
        order.MarkAccepted("paper-order", occurredAt);
        repository.Store(order);
        await context.SaveChangesAsync();
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

    private static ReserveSpotOrder CreateReserveHandler(TradingBotDbContext context) =>
        new(
            new OrderRepository(context),
            new PortfolioRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static ProcessPaperOrderSnapshot CreateProcessHandler(TradingBotDbContext context)
    {
        var fill = new ApplySpotOrderFill(
            new OrderRepository(context),
            new PortfolioRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());
        return new ProcessPaperOrderSnapshot(new PaperOrderReader(context), fill);
    }

    private static async Task CleanupAsync(
        string connectionString,
        Guid orderId,
        string exchange,
        IReadOnlyCollection<string> correlations)
    {
        await using var cleanup = CreateContext(connectionString);
        await cleanup.AuditEvents.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        await cleanup.OutboxMessages.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        await cleanup.SpotExecutions.Where(x => x.OrderId == orderId).ExecuteDeleteAsync();
        await cleanup.SpotOrderReservations.Where(x => x.OrderId == orderId).ExecuteDeleteAsync();
        await cleanup.SpotPositions.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.AssetBalances.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.Orders.Where(x => x.Id == orderId).ExecuteDeleteAsync();
    }
}
