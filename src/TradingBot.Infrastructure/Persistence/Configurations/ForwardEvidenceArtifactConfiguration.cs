using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class ForwardEvidenceArtifactConfiguration :
    IEntityTypeConfiguration<ForwardEvidenceArtifactEntity>
{
    public void Configure(EntityTypeBuilder<ForwardEvidenceArtifactEntity> builder)
    {
        builder.ToTable("ForwardEvidenceArtifacts", "research");
        builder.HasKey(static artifact => artifact.WindowSha256);
        builder.HasIndex(static artifact => new
        {
            artifact.PipelineId,
            artifact.WindowIndex
        }).IsUnique();
        builder.Property(static artifact => artifact.WindowSha256)
            .HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(static artifact => artifact.PipelineId)
            .HasMaxLength(128).IsUnicode(false);
        ConfigureHash(builder.Property(static artifact => artifact.ManifestSha256));
        ConfigureHash(builder.Property(static artifact => artifact.SignalSha256));
        ConfigureHash(builder.Property(static artifact => artifact.TrendSha256));
        ConfigurePath(builder.Property(static artifact => artifact.ManifestPath));
        ConfigurePath(builder.Property(static artifact => artifact.SignalPath));
        ConfigurePath(builder.Property(static artifact => artifact.TrendPath));
        builder.Property(static artifact => artifact.SignalSourceId)
            .HasMaxLength(128).IsUnicode(false);
        builder.Property(static artifact => artifact.TrendSourceId)
            .HasMaxLength(128).IsUnicode(false);
        builder.Property(static artifact => artifact.StartInclusive).HasPrecision(7);
        builder.Property(static artifact => artifact.EndExclusive).HasPrecision(7);
        builder.Property(static artifact => artifact.SealedAt).HasPrecision(7);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_ForwardEvidenceArtifacts_Window",
                "[WindowIndex] >= 0 AND [EndExclusive] > [StartInclusive] AND [SealedAt] >= [EndExclusive]");
            table.HasCheckConstraint(
                "CK_ForwardEvidenceArtifacts_Counts",
                "[SignalCandleCount] = 2880 AND [TrendCandleCount] = 720 AND [SignalTimeframeSeconds] = 900 AND [TrendTimeframeSeconds] = 3600");
        });
    }

    private static void ConfigureHash(PropertyBuilder<string> property) =>
        property.HasMaxLength(64).IsUnicode(false).IsFixedLength();

    private static void ConfigurePath(PropertyBuilder<string> property) =>
        property.HasMaxLength(2048).IsUnicode(false);
}
