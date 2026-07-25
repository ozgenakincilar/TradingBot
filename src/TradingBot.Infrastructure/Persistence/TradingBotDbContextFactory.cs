using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradingBot.Infrastructure.Persistence;

public sealed class TradingBotDbContextFactory : IDesignTimeDbContextFactory<TradingBotDbContext>
{
    private const string ConnectionEnvironmentVariable = "TRADINGBOT_DB_CONNECTION";

    public TradingBotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionEnvironmentVariable} before running EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<TradingBotDbContext>()
            .UseSqlServer(
                connectionString,
                static sqlServer => sqlServer.MigrationsAssembly(typeof(TradingBotDbContext).Assembly.FullName))
            .Options;

        return new TradingBotDbContext(options);
    }
}
