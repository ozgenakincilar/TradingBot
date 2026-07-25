using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;
using TradingBot.Infrastructure.Persistence.Repositories;

namespace TradingBot.Infrastructure.Tests;

public sealed class SpotOrderReservationIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";

    [Fact]
    public async Task PartialBuyAndCancel_PersistsOnlyRemainingReleaseExactlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid().ToString("N");
        var exchange = $"R{unique[..12]}".ToUpperInvariant();
        var order = CreateRiskApprovedOrder(exchange, unique);
        var occurredAt = order.UpdatedAt;
        var correlations = new[]
        {
            $"reserve-{unique}",
            $"fill-{unique}",
            $"cancel-{unique}"
        };

        try
        {
            await SeedAsync(connectionString, order);

            await using (var reserveContext = CreateContext(connectionString))
            {
                var result = await CreateReserveHandler(reserveContext).HandleAsync(
                    new ReserveSpotOrderCommand(
                        order.Id,
                        AssetCode.Create("BTC"),
                        AssetCode.Create("USDT"),
                        Price.From(100m),
                        Money.Create(2m, "USDT"),
                        occurredAt.AddSeconds(1),
                        correlations[0]),
                    CancellationToken.None);
                Assert.Equal(SpotReservationOperationResult.Applied, result);
            }

            await MarkOrderOpenAsync(connectionString, order.Id, occurredAt.AddSeconds(2));

            var fillCommand = new ApplySpotOrderFillCommand(
                order.Id,
                $"execution-{unique}",
                Quantity.From(1m),
                Price.From(90m),
                Money.Create(1m, "USDT"),
                occurredAt.AddSeconds(3),
                correlations[1]);
            await using (var fillContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    SpotReservationOperationResult.Applied,
                    await CreateFillHandler(fillContext).HandleAsync(fillCommand, CancellationToken.None));
            }

            await using (var duplicateContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    SpotReservationOperationResult.AlreadyApplied,
                    await CreateFillHandler(duplicateContext).HandleAsync(fillCommand, CancellationToken.None));
            }

            await using (var cancelContext = CreateContext(connectionString))
            {
                Assert.Equal(
                    SpotReservationOperationResult.Applied,
                    await CreateCancelHandler(cancelContext).HandleAsync(
                        new CancelSpotOrderReservationCommand(
                            order.Id,
                            occurredAt.AddSeconds(4),
                            correlations[2]),
                        CancellationToken.None));
            }

            await using var verification = CreateContext(connectionString);
            var storedOrder = await verification.Orders.SingleAsync(candidate => candidate.Id == order.Id.Value);
            var quote = await verification.AssetBalances.SingleAsync(
                balance => balance.Exchange == exchange && balance.Asset == "USDT");
            var baseBalance = await verification.AssetBalances.SingleAsync(
                balance => balance.Exchange == exchange && balance.Asset == "BTC");
            var position = await verification.SpotPositions.SingleAsync(
                candidate => candidate.Exchange == exchange && candidate.Symbol == "BTCUSDT");
            var reservation = await verification.SpotOrderReservations.SingleAsync(
                candidate => candidate.OrderId == order.Id.Value);

            Assert.Equal((byte)OrderStatus.Cancelled, storedOrder.Status);
            Assert.Equal(1m, storedOrder.FilledQuantity);
            Assert.Equal(909m, quote.Total);
            Assert.Equal(0m, quote.Reserved);
            Assert.Equal(1m, baseBalance.Total);
            Assert.Equal(1m, position.OpenQuantity);
            Assert.Equal(91m, position.AverageEntryPrice);
            Assert.Equal((byte)SpotReservationStatus.Cancelled, reservation.Status);
            Assert.Equal(1m, reservation.FilledQuantity);
            Assert.Equal(0m, reservation.RemainingReserved);
            Assert.Equal(1, await verification.SpotExecutions.CountAsync(x => x.OrderId == order.Id.Value));
            Assert.Equal(3, await verification.AuditEvents.CountAsync(x => correlations.Contains(x.CorrelationId)));
            Assert.Equal(3, await verification.OutboxMessages.CountAsync(x => correlations.Contains(x.CorrelationId)));
        }
        finally
        {
            await CleanupAsync(connectionString, order.Id.Value, correlations);
        }
    }

    private static Order CreateRiskApprovedOrder(string exchange, string unique)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(
            OrderId.New(),
            ClientOrderId.Create($"TEST-RES-{unique}"),
            InstrumentId.Create(exchange, "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(2m),
            Price.From(100m),
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

    private static async Task MarkOrderOpenAsync(
        string connectionString,
        OrderId orderId,
        DateTimeOffset occurredAt)
    {
        await using var context = CreateContext(connectionString);
        var repository = new OrderRepository(context);
        var order = await repository.GetAsync(orderId, CancellationToken.None)
            ?? throw new InvalidOperationException("Seeded order was not found.");
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

    private static ApplySpotOrderFill CreateFillHandler(TradingBotDbContext context) =>
        new(
            new OrderRepository(context),
            new PortfolioRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static CancelSpotOrderReservation CreateCancelHandler(TradingBotDbContext context) =>
        new(
            new OrderRepository(context),
            new PortfolioRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
            new TradingUnitOfWork(context),
            new SystemIdGenerator());

    private static async Task CleanupAsync(
        string connectionString,
        Guid orderId,
        IReadOnlyCollection<string> correlations)
    {
        await using var cleanup = CreateContext(connectionString);
        await cleanup.AuditEvents.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        await cleanup.OutboxMessages.Where(x => correlations.Contains(x.CorrelationId)).ExecuteDeleteAsync();
        await cleanup.SpotExecutions.Where(x => x.OrderId == orderId).ExecuteDeleteAsync();
        await cleanup.SpotOrderReservations.Where(x => x.OrderId == orderId).ExecuteDeleteAsync();
        var order = await cleanup.Orders.SingleOrDefaultAsync(x => x.Id == orderId);
        if (order is not null)
        {
            await cleanup.SpotPositions.Where(x => x.Exchange == order.Exchange).ExecuteDeleteAsync();
            await cleanup.AssetBalances.Where(x => x.Exchange == order.Exchange).ExecuteDeleteAsync();
            await cleanup.Orders.Where(x => x.Id == orderId).ExecuteDeleteAsync();
        }
    }
}
