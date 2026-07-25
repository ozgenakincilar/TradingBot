using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRunEntity>
{
    public void Configure(EntityTypeBuilder<ReconciliationRunEntity> builder)
    {
        builder.ToTable("ReconciliationRuns", "operations");
        builder.HasKey(static run => new { run.Exchange, run.SnapshotId });
        builder.HasIndex(static run => new { run.Exchange, run.SnapshotOccurredAt });
        builder.Property(static run => run.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static run => run.SnapshotId).HasMaxLength(128).IsUnicode(false);
        builder.Property(static run => run.SnapshotHash).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static run => run.SnapshotOccurredAt).HasPrecision(7);
        builder.Property(static run => run.DiscrepanciesJson).HasColumnType("nvarchar(max)");
        builder.Property(static run => run.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ReconciliationRuns_DiscrepancyCount",
            "[DiscrepancyCount] >= 0"));
    }
}
