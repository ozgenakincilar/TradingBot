using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class SpotExecutionConfiguration : IEntityTypeConfiguration<SpotExecutionEntity>
{
    public void Configure(EntityTypeBuilder<SpotExecutionEntity> builder)
    {
        builder.ToTable("SpotExecutions", "portfolio");
        builder.HasKey(static execution => new
        {
            execution.Exchange,
            execution.ExchangeExecutionId
        });
        builder.HasIndex(static execution => new { execution.Exchange, execution.Symbol, execution.OccurredAt });
        builder.HasIndex(static execution => execution.OrderId);
        builder.HasOne<ExecutionOrderEntity>()
            .WithMany()
            .HasForeignKey(static execution => execution.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(static execution => execution.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static execution => execution.ExchangeExecutionId).HasMaxLength(128).IsUnicode(false);
        builder.Property(static execution => execution.Symbol).HasMaxLength(32).IsUnicode(false);
        builder.Property(static execution => execution.Quantity).HasPrecision(38, 18);
        builder.Property(static execution => execution.Price).HasPrecision(38, 18);
        builder.Property(static execution => execution.QuoteFee).HasPrecision(38, 18);
        builder.Property(static execution => execution.RealizedPnl).HasPrecision(38, 18);
        builder.Property(static execution => execution.OccurredAt).HasPrecision(7);
        builder.Property(static execution => execution.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_SpotExecutions_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_SpotExecutions_Price", "[Price] > 0");
            table.HasCheckConstraint("CK_SpotExecutions_QuoteFee", "[QuoteFee] >= 0");
        });
    }
}
