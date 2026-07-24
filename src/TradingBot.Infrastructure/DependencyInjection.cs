using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Infrastructure.Persistence;

namespace TradingBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTradingBotPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<TradingBotDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                static sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(TradingBotDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(maxRetryCount: 3);
                }));

        return services;
    }
}
