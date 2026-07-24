using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        builder.ToTable("AuditEvents", "operations");
        builder.HasKey(static auditEvent => auditEvent.Id);
        builder.HasIndex(static auditEvent => new
        {
            auditEvent.AggregateType,
            auditEvent.AggregateId,
            auditEvent.OccurredAt
        });

        builder.Property(static auditEvent => auditEvent.Category).HasMaxLength(64).IsUnicode(false);
        builder.Property(static auditEvent => auditEvent.Action).HasMaxLength(128).IsUnicode(false);
        builder.Property(static auditEvent => auditEvent.AggregateType).HasMaxLength(128).IsUnicode(false);
        builder.Property(static auditEvent => auditEvent.AggregateId).HasMaxLength(128).IsUnicode(false);
        builder.Property(static auditEvent => auditEvent.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.Property(static auditEvent => auditEvent.Data).HasColumnType("nvarchar(max)");
        builder.Property(static auditEvent => auditEvent.OccurredAt).HasPrecision(7);
    }
}
