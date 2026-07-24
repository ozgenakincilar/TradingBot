using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Repositories;

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

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IRiskDecisionRepository, RiskDecisionRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<ITradingUnitOfWork, TradingUnitOfWork>();
        services.AddSingleton<IIdGenerator, SystemIdGenerator>();

        return services;
    }
}
