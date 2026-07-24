using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class SpotPositionConfiguration : IEntityTypeConfiguration<SpotPositionEntity>
{
    public void Configure(EntityTypeBuilder<SpotPositionEntity> builder)
    {
        builder.ToTable("SpotPositions", "portfolio");
        builder.HasKey(static position => new { position.Exchange, position.Symbol });
        builder.Property(static position => position.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static position => position.Symbol).HasMaxLength(32).IsUnicode(false);
        builder.Property(static position => position.BaseAsset).HasMaxLength(12).IsUnicode(false);
        builder.Property(static position => position.QuoteAsset).HasMaxLength(12).IsUnicode(false);
        builder.Property(static position => position.OpenQuantity).HasPrecision(38, 18);
        builder.Property(static position => position.ReservedSellQuantity).HasPrecision(38, 18);
        builder.Property(static position => position.AverageEntryPrice).HasPrecision(38, 18);
        builder.Property(static position => position.RealizedPnl).HasPrecision(38, 18);
        builder.Property(static position => position.UpdatedAt).HasPrecision(7);
        builder.Property(static position => position.RowVersion).IsRowVersion();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_SpotPositions_OpenQuantity", "[OpenQuantity] >= 0");
            table.HasCheckConstraint(
                "CK_SpotPositions_ReservedSellQuantity",
                "[ReservedSellQuantity] >= 0 AND [ReservedSellQuantity] <= [OpenQuantity]");
            table.HasCheckConstraint(
                "CK_SpotPositions_AverageEntryPrice",
                "([OpenQuantity] = 0 AND [AverageEntryPrice] = 0) OR ([OpenQuantity] > 0 AND [AverageEntryPrice] > 0)");
        });
    }
}
