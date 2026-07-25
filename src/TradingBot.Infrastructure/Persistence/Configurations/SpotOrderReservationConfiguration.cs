using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class SpotOrderReservationConfiguration : IEntityTypeConfiguration<SpotOrderReservationEntity>
{
    public void Configure(EntityTypeBuilder<SpotOrderReservationEntity> builder)
    {
        builder.ToTable("SpotOrderReservations", "portfolio");
        builder.HasKey(static reservation => reservation.OrderId);
        builder.HasIndex(static reservation => new
        {
            reservation.Exchange,
            reservation.Symbol,
            reservation.Status
        });
        builder.HasOne<ExecutionOrderEntity>()
            .WithOne()
            .HasForeignKey<SpotOrderReservationEntity>(static reservation => reservation.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(static reservation => reservation.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static reservation => reservation.Symbol).HasMaxLength(32).IsUnicode(false);
        builder.Property(static reservation => reservation.BaseAsset).HasMaxLength(12).IsUnicode(false);
        builder.Property(static reservation => reservation.QuoteAsset).HasMaxLength(12).IsUnicode(false);
        builder.Property(static reservation => reservation.ApprovedQuantity).HasPrecision(38, 18);
        builder.Property(static reservation => reservation.FilledQuantity).HasPrecision(38, 18);
        builder.Property(static reservation => reservation.RemainingReserved).HasPrecision(38, 18);
        builder.Property(static reservation => reservation.CreatedAt).HasPrecision(7);
        builder.Property(static reservation => reservation.UpdatedAt).HasPrecision(7);
        builder.Property(static reservation => reservation.RowVersion).IsRowVersion();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_SpotOrderReservations_ApprovedQuantity", "[ApprovedQuantity] > 0");
            table.HasCheckConstraint(
                "CK_SpotOrderReservations_FilledQuantity",
                "[FilledQuantity] >= 0 AND [FilledQuantity] <= [ApprovedQuantity]");
            table.HasCheckConstraint("CK_SpotOrderReservations_RemainingReserved", "[RemainingReserved] >= 0");
        });
    }
}
