namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class ForwardEvidenceEvaluationEntity
{
    public required string RunSha256 { get; set; }

    public required string PipelineId { get; set; }

    public int SealedWindowCount { get; set; }

    public required string ReportSha256 { get; set; }

    public required string ReportPath { get; set; }

    public required string ReportFileSha256 { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    public bool MinimumTradesPassed { get; set; }

    public bool ProfitFactorPassed { get; set; }

    public bool PositiveNetReturnPassed { get; set; }

    public bool BenchmarkExcessPassed { get; set; }

    public bool DrawdownPassed { get; set; }

    public bool ProfitableWindowsPassed { get; set; }

    public bool ExecutionCostCoveragePassed { get; set; }

    public bool FullyExecutedPassed { get; set; }

    public bool IsAccepted { get; set; }
}
