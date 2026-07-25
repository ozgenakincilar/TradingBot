using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class WalkForwardRunConfiguration : IEntityTypeConfiguration<WalkForwardRunEntity>
{
    public void Configure(EntityTypeBuilder<WalkForwardRunEntity> builder)
    {
        builder.ToTable("WalkForwardRuns", "research");
        builder.HasKey(static run => run.RunSha256);
        builder.HasIndex(static run => run.ReportSha256).IsUnique();
        builder.HasIndex(static run => new { run.ScheduleSha256, run.CreatedAt });
        builder.Property(static run => run.RunSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static run => run.ScheduleSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static run => run.ReportSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static run => run.SchemaVersion).HasMaxLength(64).IsUnicode(false);
        builder.Property(static run => run.StrategyId).HasMaxLength(128).IsUnicode(false);
        builder.Property(static run => run.TotalFees).HasPrecision(38, 18);
        builder.Property(static run => run.MeanNetReturnPercent).HasPrecision(38, 18);
        builder.Property(static run => run.MedianNetReturnPercent).HasPrecision(38, 18);
        builder.Property(static run => run.WorstNetReturnPercent).HasPrecision(38, 18);
        builder.Property(static run => run.BestNetReturnPercent).HasPrecision(38, 18);
        builder.Property(static run => run.CompoundedNetReturnPercent).HasPrecision(38, 18);
        builder.Property(static run => run.MeanMaximumDrawdownPercent).HasPrecision(38, 18);
        builder.Property(static run => run.CreatedAt).HasPrecision(7);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_WalkForwardRuns_StrategyVersion", "[StrategyVersion] > 0");
            table.HasCheckConstraint("CK_WalkForwardRuns_TrainingMode", "[TrainingMode] IN (1, 2)");
            table.HasCheckConstraint(
                "CK_WalkForwardRuns_Durations",
                "[TrainingDurationTicks] > 0 AND [ValidationDurationTicks] > 0 AND [OutOfSampleDurationTicks] > 0");
            table.HasCheckConstraint(
                "CK_WalkForwardRuns_WindowCounts",
                "[WindowCount] > 0 AND [ProfitableWindowCount] >= 0 AND [ProfitableWindowCount] <= [WindowCount]");
            table.HasCheckConstraint(
                "CK_WalkForwardRuns_TradeCount",
                "[TotalCompletedTradeCount] >= 0");
            table.HasCheckConstraint("CK_WalkForwardRuns_TotalFees", "[TotalFees] >= 0");
            table.HasCheckConstraint(
                "CK_WalkForwardRuns_MeanDrawdown",
                "[MeanMaximumDrawdownPercent] >= 0 AND [MeanMaximumDrawdownPercent] <= 100");
        });
    }
}
