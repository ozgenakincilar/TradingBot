using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class WalkForwardWindowResultConfiguration
    : IEntityTypeConfiguration<WalkForwardWindowResultEntity>
{
    public void Configure(EntityTypeBuilder<WalkForwardWindowResultEntity> builder)
    {
        builder.ToTable("WalkForwardWindowResults", "research");
        builder.HasKey(static result => new { result.RunSha256, result.WindowIndex });
        builder.HasIndex(static result => result.ManifestSha256);
        builder.Property(static result => result.RunSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static result => result.ManifestSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static result => result.TrainStartInclusive).HasPrecision(7);
        builder.Property(static result => result.TrainEndExclusive).HasPrecision(7);
        builder.Property(static result => result.ValidationEndExclusive).HasPrecision(7);
        builder.Property(static result => result.OutOfSampleEndExclusive).HasPrecision(7);
        builder.Property(static result => result.FirstFillAt).HasPrecision(7);
        builder.Property(static result => result.LastFillAt).HasPrecision(7);
        ConfigureFinancialPrecision(builder);
        builder.HasOne(static result => result.Run)
            .WithMany(static run => run.Windows)
            .HasForeignKey(static result => result.RunSha256)
            .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_WalkForwardWindowResults_Index", "[WindowIndex] >= 0");
            table.HasCheckConstraint(
                "CK_WalkForwardWindowResults_Times",
                "[TrainStartInclusive] < [TrainEndExclusive] AND [TrainEndExclusive] < [ValidationEndExclusive] AND [ValidationEndExclusive] < [OutOfSampleEndExclusive]");
            table.HasCheckConstraint(
                "CK_WalkForwardWindowResults_Balances",
                "[InitialQuoteBalance] > 0 AND [EndingCashBalance] >= 0 AND [OpenQuantity] >= 0 AND [NetLiquidationValue] >= 0");
            table.HasCheckConstraint(
                "CK_WalkForwardWindowResults_Costs",
                "[GrossProfit] >= 0 AND [GrossLoss] >= 0 AND [TotalFees] >= 0 AND [EstimatedSpreadCost] >= 0 AND [EstimatedSlippageCost] >= 0");
            table.HasCheckConstraint(
                "CK_WalkForwardWindowResults_Counts",
                "[FillCount] >= 0 AND [CompletedTradeCount] >= 0 AND [WinningTradeCount] >= 0 AND [WinningTradeCount] <= [CompletedTradeCount]");
            table.HasCheckConstraint(
                "CK_WalkForwardWindowResults_Drawdown",
                "[MaximumDrawdownPercent] >= 0 AND [MaximumDrawdownPercent] <= 100");
        });
    }

    private static void ConfigureFinancialPrecision(
        EntityTypeBuilder<WalkForwardWindowResultEntity> builder)
    {
        string[] properties =
        [
            nameof(WalkForwardWindowResultEntity.InitialQuoteBalance),
            nameof(WalkForwardWindowResultEntity.EndingCashBalance),
            nameof(WalkForwardWindowResultEntity.OpenQuantity),
            nameof(WalkForwardWindowResultEntity.NetLiquidationValue),
            nameof(WalkForwardWindowResultEntity.GrossReturnPercent),
            nameof(WalkForwardWindowResultEntity.NetReturnPercent),
            nameof(WalkForwardWindowResultEntity.RealizedPnl),
            nameof(WalkForwardWindowResultEntity.GrossProfit),
            nameof(WalkForwardWindowResultEntity.GrossLoss),
            nameof(WalkForwardWindowResultEntity.Expectancy),
            nameof(WalkForwardWindowResultEntity.TotalFees),
            nameof(WalkForwardWindowResultEntity.EstimatedSpreadCost),
            nameof(WalkForwardWindowResultEntity.EstimatedSlippageCost),
            nameof(WalkForwardWindowResultEntity.MaximumDrawdownPercent),
            nameof(WalkForwardWindowResultEntity.WinRatePercent),
            nameof(WalkForwardWindowResultEntity.ProfitFactor)
        ];
        foreach (var property in properties)
        {
            builder.Property(property).HasPrecision(38, 18);
        }
    }
}
