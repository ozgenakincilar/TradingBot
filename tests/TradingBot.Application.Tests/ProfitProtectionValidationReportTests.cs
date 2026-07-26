using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class ProfitProtectionValidationReportTests
{
    private static readonly DateTimeOffset Start =
        new(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));

    [Fact]
    public void CandidatePassingAllPreRegisteredGatesIsAcceptedDeterministically()
    {
        var schedule = Schedule();
        var result = Result(
            schedule.Windows[0],
            candidateNet: 1m,
            candidateTrades: 7,
            candidateCostEach: 3m,
            candidateDrawdown: 2m,
            candidateGivebacks: 1);

        var first = ProfitProtectionValidationReportFactory.Create(schedule, [result]);
        var second = ProfitProtectionValidationReportFactory.Create(schedule, [result]);

        Assert.True(first.Acceptance.IsAccepted);
        Assert.Equal(30m, first.TradeReductionPercent);
        Assert.Equal(40m, first.CostReductionPercent);
        Assert.True(first.FavorableGivebackRateReductionPercent > 70m);
        Assert.Equal(1m, first.CandidateCompoundedNetReturnPercent);
        Assert.Equal(0.5m, first.CandidateBenchmarkExcessPercent);
        Assert.Equal(first.RunSha256, second.RunSha256);
        Assert.Equal(first.ReportSha256, second.ReportSha256);
    }

    [Fact]
    public void CandidateFailingAnyGateRemainsRejected()
    {
        var schedule = Schedule();
        var result = Result(
            schedule.Windows[0],
            candidateNet: -1m,
            candidateTrades: 9,
            candidateCostEach: 4.8m,
            candidateDrawdown: 6m,
            candidateGivebacks: 5);

        var report = ProfitProtectionValidationReportFactory.Create(schedule, [result]);

        Assert.False(report.Acceptance.IsAccepted);
        Assert.False(report.Acceptance.TradeReductionPassed);
        Assert.False(report.Acceptance.CostReductionPassed);
        Assert.False(report.Acceptance.FavorableGivebackReductionPassed);
        Assert.False(report.Acceptance.PositiveNetReturnPassed);
        Assert.False(report.Acceptance.BenchmarkExcessPassed);
        Assert.False(report.Acceptance.DrawdownPassed);
        Assert.False(report.Acceptance.ProfitableWindowsPassed);
    }

    private static ProfitProtectionValidationWindow Result(
        WalkForwardWindow window,
        decimal candidateNet,
        int candidateTrades,
        decimal candidateCostEach,
        decimal candidateDrawdown,
        int candidateGivebacks) => new(
        window.Index,
        Manifest(DefinitionV2(), window.Split),
        Diagnostics(Execution(-1m, 10, 5m, 2m), givebacks: 5, 'B'),
        Manifest(DefinitionV3(), window.Split),
        Diagnostics(
            Execution(candidateNet, candidateTrades, candidateCostEach, candidateDrawdown),
            candidateGivebacks,
            'C'),
        new BuyAndHoldBenchmarkReport(
            1_000m, 100m, 900m, 1m, 100m, 105m, 1_005m,
            GrossReturnPercent: 0.5m, NetReturnPercent: 0.5m,
            TotalFees: 0m, EstimatedSpreadCost: 0m, EstimatedSlippageCost: 0m,
            MaximumDrawdownPercent: 1m, CandleCount: 2_880,
            EntryAt: window.Split.TrainEndExclusive,
            ExitAt: window.Split.ValidationEndExclusive));

    private static BacktestExecutionDiagnosticsReport Diagnostics(
        BacktestExecutionReport execution,
        int givebacks,
        char hash)
    {
        var trades = Enumerable.Range(0, execution.CompletedTradeCount)
            .Select(index => new BacktestTradeAttribution(
                index + 1,
                Start.AddHours(index),
                Start.AddHours(index + 1),
                "signal-ema-hysteresis-cross-up",
                "signal-ema-hysteresis-cross-down",
                100m,
                99m,
                1m,
                -1m,
                0.1m,
                0.1m,
                0.1m,
                -0.7m,
                index < givebacks ? 1m : 0m,
                1m,
                TimeSpan.FromHours(1)))
            .ToArray();
        return new BacktestExecutionDiagnosticsReport(
            1,
            new string(hash, 64),
            execution,
            trades,
            trades.Average(static trade => trade.MaximumFavorableExcursionPercent),
            1m,
            givebacks);
    }

    private static BacktestExecutionReport Execution(
        decimal netReturn,
        int trades,
        decimal costEach,
        decimal drawdown)
    {
        const decimal initial = 1_000m;
        var liquidation = initial * (1m + netReturn / 100m);
        var costs = costEach * 3m;
        return new BacktestExecutionReport(
            initial, liquidation, 0m, liquidation,
            GrossReturnPercent: netReturn + costs / initial * 100m,
            NetReturnPercent: netReturn, RealizedPnl: liquidation - initial,
            GrossProfit: 0m, GrossLoss: 0m, Expectancy: trades == 0 ? null : 0m,
            TotalFees: costEach, EstimatedSpreadCost: costEach,
            EstimatedSlippageCost: costEach, MaximumDrawdownPercent: drawdown,
            FillCount: trades * 2, CompletedTradeCount: trades,
            WinningTradeCount: 0, WinRatePercent: trades == 0 ? null : 0m,
            ProfitFactor: null,
            AverageHoldingTime: trades == 0 ? null : TimeSpan.FromHours(1),
            HasPendingExecution: false, FirstFillAt: null, LastFillAt: null);
    }

    private static BacktestRunManifest Manifest(
        StrategyDefinition definition,
        ChronologicalDatasetSplit split) => BacktestRunManifestFactory.Create(
        definition,
        Policy(),
        new HistoricalCandleDatasetDescriptor(
            "signal-2023", HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string('A', 64), Instrument, Signal),
        new HistoricalCandleDatasetSummary(35_040, Start, Start.AddDays(240)),
        new HistoricalCandleDatasetDescriptor(
            "trend-2023", HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string('D', 64), Instrument, Trend),
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

    private static StrategyDefinition DefinitionV2() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline", 2, Instrument, Signal, Trend,
        20, 200, 2m, 200, 200, signalEmaHysteresisBasisPoints: 30m);

    private static StrategyDefinition DefinitionV3() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline", 3, Instrument, Signal, Trend,
        20, 200, 2m, 200, 200,
        signalEmaHysteresisBasisPoints: 30m,
        reentryCooldownCandles: 4,
        profitProtectionActivationBasisPoints: 100m,
        profitProtectionTrailingBasisPoints: 50m);

    private static BacktestExecutionPolicy Policy() => new(
        1_000m, AssetCode.Create("BTC"), AssetCode.Create("USDT"),
        Percentage.FromPercent(10m), 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100), Percentage.FromPercent(0.1m), 10m,
            Percentage.FromPercent(5m)));
}
