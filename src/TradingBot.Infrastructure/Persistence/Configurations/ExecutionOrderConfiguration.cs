using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class ExecutionOrderConfiguration : IEntityTypeConfiguration<ExecutionOrderEntity>
{
    public void Configure(EntityTypeBuilder<ExecutionOrderEntity> builder)
    {
        builder.ToTable("Orders", "execution");
        builder.HasKey(static order => order.Id);
        builder.HasIndex(static order => order.ClientOrderId).IsUnique();
        builder.HasIndex(static order => new { order.Exchange, order.Symbol, order.Status });

        builder.Property(static order => order.ClientOrderId).HasMaxLength(64).IsUnicode(false);
        builder.Property(static order => order.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static order => order.Symbol).HasMaxLength(32).IsUnicode(false);
        builder.Property(static order => order.RequestedQuantity).HasPrecision(38, 18);
        builder.Property(static order => order.ApprovedQuantity).HasPrecision(38, 18);
        builder.Property(static order => order.LimitPrice).HasPrecision(38, 18);
        builder.Property(static order => order.FilledQuantity).HasPrecision(38, 18);
        builder.Property(static order => order.AverageFillPrice).HasPrecision(38, 18);
        builder.Property(static order => order.ExchangeOrderId).HasMaxLength(128).IsUnicode(false);
        builder.Property(static order => order.RejectionReason).HasMaxLength(512);
        builder.Property(static order => order.CreatedAt).HasPrecision(7);
        builder.Property(static order => order.UpdatedAt).HasPrecision(7);
        builder.Property(static order => order.RowVersion).IsRowVersion();
    }
}
