namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class WalkForwardRunEntity
{
    public required string RunSha256 { get; set; }

    public required string ScheduleSha256 { get; set; }

    public required string ReportSha256 { get; set; }

    public required string SchemaVersion { get; set; }

    public required string StrategyId { get; set; }

    public int StrategyVersion { get; set; }

    public int TrainingMode { get; set; }

    public long TrainingDurationTicks { get; set; }

    public long ValidationDurationTicks { get; set; }

    public long OutOfSampleDurationTicks { get; set; }

    public int WindowCount { get; set; }

    public int ProfitableWindowCount { get; set; }

    public int TotalCompletedTradeCount { get; set; }

    public decimal TotalFees { get; set; }

    public decimal MeanNetReturnPercent { get; set; }

    public decimal MedianNetReturnPercent { get; set; }

    public decimal WorstNetReturnPercent { get; set; }

    public decimal BestNetReturnPercent { get; set; }

    public decimal CompoundedNetReturnPercent { get; set; }

    public decimal MeanMaximumDrawdownPercent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<WalkForwardWindowResultEntity> Windows { get; } = [];
}
