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

public sealed class PortfolioPersistenceIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";

    [Fact]
    public async Task CompletedBuyFill_CommitsPortfolioLedgerAuditAndOutboxExactlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var unique = Guid.NewGuid().ToString("N");
        var exchange = $"T{unique[..12]}".ToUpperInvariant();
        var executionId = $"fill-{unique}";
        var correlationId = $"test-{unique}";
        var instrument = InstrumentId.Create(exchange, "BTCUSDT");
        var occurredAt = DateTimeOffset.UtcNow;

        try
        {
            await using (var seed = CreateContext(connectionString))
            {
                seed.AssetBalances.Add(new AssetBalanceEntity
                {
                    Exchange = exchange,
                    Asset = "USDT",
                    Total = 1_000m,
                    Reserved = 0m,
                    UpdatedAt = occurredAt.AddSeconds(-1)
                });
                await seed.SaveChangesAsync();
            }

            await using (var context = CreateContext(connectionString))
            {
                var handler = CreateHandler(context);
                var command = new PersistCompletedSpotFillCommand(
                    executionId,
                    instrument,
                    AssetCode.Create("BTC"),
                    AssetCode.Create("USDT"),
                    OrderSide.Buy,
                    Quantity.From(2m),
                    Price.From(100m),
                    Money.Create(1m, "USDT"),
                    occurredAt,
                    correlationId);

                Assert.Equal(
                    PersistSpotFillResult.Applied,
                    await handler.HandleAsync(command, CancellationToken.None));
                Assert.Equal(
                    PersistSpotFillResult.AlreadyApplied,
                    await handler.HandleAsync(command, CancellationToken.None));
            }

            await using var verification = CreateContext(connectionString);
            Assert.Equal(
                799m,
                (await verification.AssetBalances.SingleAsync(
                    balance => balance.Exchange == exchange && balance.Asset == "USDT")).Total);
            Assert.Equal(
                2m,
                (await verification.AssetBalances.SingleAsync(
                    balance => balance.Exchange == exchange && balance.Asset == "BTC")).Total);
            var position = await verification.SpotPositions.SingleAsync(
                candidate => candidate.Exchange == exchange && candidate.Symbol == "BTCUSDT");
            Assert.Equal(2m, position.OpenQuantity);
            Assert.Equal(100.5m, position.AverageEntryPrice);
            Assert.Equal(
                1,
                await verification.SpotExecutions.CountAsync(
                    execution => execution.Exchange == exchange &&
                                 execution.ExchangeExecutionId == executionId));
            Assert.Equal(1, await verification.AuditEvents.CountAsync(x => x.CorrelationId == correlationId));
            Assert.Equal(1, await verification.OutboxMessages.CountAsync(x => x.CorrelationId == correlationId));
        }
        finally
        {
            await CleanupAsync(connectionString, exchange, correlationId);
        }
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

    private static PersistCompletedSpotFill CreateHandler(TradingBotDbContext context) =>
        new(
            new PortfolioRepository(context),
            new AuditRepository(context),
            new OutboxRepository(context),
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
        await cleanup.SpotExecutions.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.SpotPositions.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
        await cleanup.AssetBalances.Where(x => x.Exchange == exchange).ExecuteDeleteAsync();
    }
}
