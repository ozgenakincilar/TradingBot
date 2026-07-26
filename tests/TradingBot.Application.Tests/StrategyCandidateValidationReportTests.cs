using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class StrategyCandidateValidationReportTests
{
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));

    [Fact]
    public void CandidatePassingEveryPreRegisteredGateIsAcceptedDeterministically()
    {
        var schedule = Schedule();
        var result = Result(schedule.Windows[0], candidateNet: 1m, candidateTrades: 5,
            candidateCostEach: 2m, candidateDrawdown: 2m);

        var first = StrategyCandidateValidationReportFactory.Create(schedule, [result]);
        var second = StrategyCandidateValidationReportFactory.Create(schedule, [result]);

        Assert.True(first.Acceptance.IsAccepted);
        Assert.Equal(50m, first.TradeReductionPercent);
        Assert.Equal(60m, first.CostReductionPercent);
        Assert.Equal(1m, first.CandidateCompoundedNetReturnPercent);
        Assert.Equal(0.5m, first.CandidateBenchmarkExcessPercent);
        Assert.Equal(first.RunSha256, second.RunSha256);
        Assert.Equal(first.ReportSha256, second.ReportSha256);
    }

    [Fact]
    public void AnyFailedGateRejectsCandidate()
    {
        var schedule = Schedule();
        var result = Result(schedule.Windows[0], candidateNet: -1m, candidateTrades: 9,
            candidateCostEach: 4.8m, candidateDrawdown: 6m);

        var report = StrategyCandidateValidationReportFactory.Create(schedule, [result]);

        Assert.False(report.Acceptance.IsAccepted);
        Assert.False(report.Acceptance.TradeReductionPassed);
        Assert.False(report.Acceptance.CostReductionPassed);
        Assert.False(report.Acceptance.PositiveNetReturnPassed);
        Assert.False(report.Acceptance.BenchmarkExcessPassed);
        Assert.False(report.Acceptance.DrawdownPassed);
        Assert.False(report.Acceptance.ProfitableWindowsPassed);
    }

    private static StrategyValidationWindowResult Result(
        WalkForwardWindow window,
        decimal candidateNet,
        int candidateTrades,
        decimal candidateCostEach,
        decimal candidateDrawdown) => new(
        window.Index,
        Manifest(Definition(1, 0m), window.Split),
        Execution(-1m, 10, 5m, 2m),
        Manifest(Definition(2, 30m), window.Split),
        Execution(candidateNet, candidateTrades, candidateCostEach, candidateDrawdown),
        new BuyAndHoldBenchmarkReport(
            1_000m, 100m, 900m, 1m, 100m, 105m, 1_005m,
            GrossReturnPercent: 0.5m, NetReturnPercent: 0.5m,
            TotalFees: 0m, EstimatedSpreadCost: 0m, EstimatedSlippageCost: 0m,
            MaximumDrawdownPercent: 1m, CandleCount: 2_880,
            EntryAt: window.Split.TrainEndExclusive,
            ExitAt: window.Split.ValidationEndExclusive));

    private static BacktestExecutionReport Execution(
        decimal netReturn,
        int trades,
        decimal costEach,
        decimal drawdown)
    {
        const decimal initial = 1_000m;
        var liquidation = initial * (1m + (netReturn / 100m));
        var costs = costEach * 3m;
        return new BacktestExecutionReport(
            initial, liquidation, 0m, liquidation,
            GrossReturnPercent: netReturn + (costs / initial * 100m),
            NetReturnPercent: netReturn, RealizedPnl: liquidation - initial,
            GrossProfit: 0m, GrossLoss: 0m, Expectancy: trades == 0 ? null : 0m,
            TotalFees: costEach, EstimatedSpreadCost: costEach,
            EstimatedSlippageCost: costEach, MaximumDrawdownPercent: drawdown,
            FillCount: trades * 2, CompletedTradeCount: trades,
            WinningTradeCount: 0, WinRatePercent: trades == 0 ? null : 0m,
            ProfitFactor: null, AverageHoldingTime: trades == 0 ? null : TimeSpan.FromHours(1),
            HasPendingExecution: false, FirstFillAt: null, LastFillAt: null);
    }

    private static BacktestRunManifest Manifest(
        StrategyDefinition definition,
        ChronologicalDatasetSplit split) => BacktestRunManifestFactory.Create(
        definition,
        Policy(),
        new HistoricalCandleDatasetDescriptor(
            "signal-2024", HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string('A', 64), Instrument, Signal),
        new HistoricalCandleDatasetSummary(35_040, Start, Start.AddDays(240)),
        new HistoricalCandleDatasetDescriptor(
            "trend-2024", HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string('B', 64), Instrument, Trend),
        new HistoricalCandleDatasetSummary(8_760, Start, Start.AddDays(240)),
        split,
        BacktestExperimentPlan.Create(
            BacktestRunPurpose.ParameterSelection,
            BacktestDatasetPartition.Train,
            BacktestDatasetPartition.Validation),
        42);

    private static WalkForwardSchedule Schedule() => WalkForwardSchedule.Create(
        Start, Start.AddDays(240), TimeSpan.FromDays(180), TimeSpan.FromDays(30),
        TimeSpan.FromDays(30), WalkForwardTrainingMode.Rolling, Signal, Trend);

    private static StrategyDefinition Definition(int version, decimal hysteresis) =>
        StrategyDefinition.Create(
            "btc-usdt-long-flat-baseline", version, Instrument, Signal, Trend,
            20, 200, 2m, 200, 200, hysteresis);

    private static BacktestExecutionPolicy Policy() => new(
        1_000m, AssetCode.Create("BTC"), AssetCode.Create("USDT"),
        Percentage.FromPercent(10m), 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100), Percentage.FromPercent(0.1m), 10m,
            Percentage.FromPercent(5m)));
}
