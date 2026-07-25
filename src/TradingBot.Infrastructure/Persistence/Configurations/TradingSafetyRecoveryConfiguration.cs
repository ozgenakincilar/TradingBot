using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class TradingSafetyRecoveryConfiguration : IEntityTypeConfiguration<TradingSafetyRecoveryEntity>
{
    public void Configure(EntityTypeBuilder<TradingSafetyRecoveryEntity> builder)
    {
        builder.ToTable("TradingSafetyRecoveries", "operations");
        builder.HasKey(static recovery => recovery.Id);
        builder.HasIndex(static recovery => new { recovery.Exchange, recovery.OccurredAt });
        builder.Property(static recovery => recovery.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static recovery => recovery.OperatorId).HasMaxLength(64);
        builder.Property(static recovery => recovery.Reason).HasMaxLength(512);
        builder.Property(static recovery => recovery.OccurredAt).HasPrecision(7);
        builder.Property(static recovery => recovery.EvidenceSnapshotIdsJson).HasColumnType("nvarchar(max)");
        builder.Property(static recovery => recovery.CorrelationId).HasMaxLength(64).IsUnicode(false);
    }
}
