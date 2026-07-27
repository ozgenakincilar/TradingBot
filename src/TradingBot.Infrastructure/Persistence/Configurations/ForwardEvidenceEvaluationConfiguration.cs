using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Configurations;

public sealed class ForwardEvidenceEvaluationConfiguration :
    IEntityTypeConfiguration<ForwardEvidenceEvaluationEntity>
{
    public void Configure(EntityTypeBuilder<ForwardEvidenceEvaluationEntity> builder)
    {
        builder.ToTable("ForwardEvidenceEvaluations", "research");
        builder.HasKey(static evaluation => evaluation.RunSha256);
        builder.HasIndex(static evaluation => evaluation.ReportSha256).IsUnique();
        builder.HasIndex(static evaluation => new
        {
            evaluation.PipelineId,
            evaluation.SealedWindowCount
        }).IsUnique();
        ConfigureHash(builder.Property(static evaluation => evaluation.RunSha256));
        ConfigureHash(builder.Property(static evaluation => evaluation.ReportSha256));
        ConfigureHash(builder.Property(static evaluation => evaluation.ReportFileSha256));
        builder.Property(static evaluation => evaluation.PipelineId)
            .HasMaxLength(128).IsUnicode(false);
        builder.Property(static evaluation => evaluation.ReportPath)
            .HasMaxLength(2048).IsUnicode(false);
        builder.Property(static evaluation => evaluation.EvaluatedAt).HasPrecision(7);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ForwardEvidenceEvaluations_Consistency",
            "[SealedWindowCount] >= 7 AND [IsAccepted] = ([MinimumTradesPassed] & [ProfitFactorPassed] & [PositiveNetReturnPassed] & [BenchmarkExcessPassed] & [DrawdownPassed] & [ProfitableWindowsPassed] & [ExecutionCostCoveragePassed] & [FullyExecutedPassed])"));
    }

    private static void ConfigureHash(PropertyBuilder<string> property) =>
        property.HasMaxLength(64).IsUnicode(false).IsFixedLength();
}
