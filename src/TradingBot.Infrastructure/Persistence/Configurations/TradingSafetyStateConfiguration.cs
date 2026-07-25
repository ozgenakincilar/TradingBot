using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class TradingSafetyStateConfiguration : IEntityTypeConfiguration<TradingSafetyStateEntity>
{
    public void Configure(EntityTypeBuilder<TradingSafetyStateEntity> builder)
    {
        builder.ToTable("TradingSafetyStates", "operations");
        builder.HasKey(static state => state.Exchange);
        builder.Property(static state => state.Exchange).HasMaxLength(32).IsUnicode(false);
        builder.Property(static state => state.HaltReason).HasMaxLength(512);
        builder.Property(static state => state.UpdatedAt).HasPrecision(7);
        builder.Property(static state => state.RowVersion).IsRowVersion();
    }
}
