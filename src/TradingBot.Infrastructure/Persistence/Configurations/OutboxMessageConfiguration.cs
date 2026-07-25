using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("OutboxMessages", "operations");
        builder.HasKey(static message => message.Id);
        builder.HasIndex(static message => new { message.ProcessedAt, message.OccurredAt });

        builder.Property(static message => message.MessageType).HasMaxLength(256).IsUnicode(false);
        builder.Property(static message => message.Payload).HasColumnType("nvarchar(max)");
        builder.Property(static message => message.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.Property(static message => message.OccurredAt).HasPrecision(7);
        builder.Property(static message => message.ProcessedAt).HasPrecision(7);
        builder.Property(static message => message.LastError).HasMaxLength(2048);
        builder.Property(static message => message.RowVersion).IsRowVersion();
    }
}
