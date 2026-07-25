using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class AssetBalanceConfiguration : IEntityTypeConfiguration<AssetBalanceEntity>
{
    public void Configure(EntityTypeBuilder<AssetBalanceEntity> builder)
    {
        builder.ToTable("AssetBalances", "portfolio");
        builder.HasKey(static balance => new { balance.Exchange, balance.Asset });
        builder.Property(static balance => balance.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static balance => balance.Asset).HasMaxLength(12).IsUnicode(false);
        builder.Property(static balance => balance.Total).HasPrecision(38, 18);
        builder.Property(static balance => balance.Reserved).HasPrecision(38, 18);
        builder.Property(static balance => balance.UpdatedAt).HasPrecision(7);
        builder.Property(static balance => balance.RowVersion).IsRowVersion();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_AssetBalances_Total", "[Total] >= 0");
            table.HasCheckConstraint(
                "CK_AssetBalances_Reserved",
                "[Reserved] >= 0 AND [Reserved] <= [Total]");
        });
    }
}
