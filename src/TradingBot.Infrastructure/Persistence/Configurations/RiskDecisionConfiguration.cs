using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class RiskDecisionConfiguration : IEntityTypeConfiguration<RiskDecisionEntity>
{
    public void Configure(EntityTypeBuilder<RiskDecisionEntity> builder)
    {
        builder.ToTable("RiskDecisions", "risk");
        builder.HasKey(static decision => decision.Id);
        builder.HasIndex(static decision => new { decision.OrderId, decision.OccurredAt });

        builder.Property(static decision => decision.ApprovedQuantity).HasPrecision(38, 18);
        builder.Property(static decision => decision.Reason).HasMaxLength(512);
        builder.Property(static decision => decision.OccurredAt).HasPrecision(7);
    }
}
