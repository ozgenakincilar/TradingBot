namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class WalkForwardWindowResultEntity
{
    public required string RunSha256 { get; set; }

    public int WindowIndex { get; set; }

    public required string ManifestSha256 { get; set; }

    public DateTimeOffset TrainStartInclusive { get; set; }

    public DateTimeOffset TrainEndExclusive { get; set; }

    public DateTimeOffset ValidationEndExclusive { get; set; }

    public DateTimeOffset OutOfSampleEndExclusive { get; set; }

    public decimal InitialQuoteBalance { get; set; }

    public decimal EndingCashBalance { get; set; }

    public decimal OpenQuantity { get; set; }

    public decimal NetLiquidationValue { get; set; }

    public decimal GrossReturnPercent { get; set; }

    public decimal NetReturnPercent { get; set; }

    public decimal RealizedPnl { get; set; }

    public decimal GrossProfit { get; set; }

    public decimal GrossLoss { get; set; }

    public decimal? Expectancy { get; set; }

    public decimal TotalFees { get; set; }

    public decimal EstimatedSpreadCost { get; set; }

    public decimal EstimatedSlippageCost { get; set; }

    public decimal MaximumDrawdownPercent { get; set; }

    public int FillCount { get; set; }

    public int CompletedTradeCount { get; set; }

    public int WinningTradeCount { get; set; }

    public decimal? WinRatePercent { get; set; }

    public decimal? ProfitFactor { get; set; }

    public long? AverageHoldingTimeTicks { get; set; }

    public bool HasPendingExecution { get; set; }

    public DateTimeOffset? FirstFillAt { get; set; }

    public DateTimeOffset? LastFillAt { get; set; }

    public WalkForwardRunEntity? Run { get; set; }
}
