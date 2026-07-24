using Microsoft.EntityFrameworkCore;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence;

public sealed class TradingBotDbContext(DbContextOptions<TradingBotDbContext> options)
    : DbContext(options)
{
    public DbSet<ExecutionOrderEntity> Orders => Set<ExecutionOrderEntity>();

    public DbSet<RiskDecisionEntity> RiskDecisions => Set<RiskDecisionEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingBotDbContext).Assembly);
    }
}
