using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record AdxRegimeValidationWindow(
    int Index,
    BacktestRunManifest BaselineManifest,
    BacktestExecutionDiagnosticsReport Baseline,
    BacktestRunManifest CandidateManifest,
    BacktestExecutionDiagnosticsReport Candidate,
    BuyAndHoldBenchmarkReport Benchmark);

public sealed record AdxRegimeValidationAcceptance(
    bool TradeReductionPassed,
    bool CostReductionPassed,
    bool MinimumTradesPassed,
    bool ProfitFactorPassed,
    bool PositiveNetReturnPassed,
    bool BenchmarkExcessPassed,
    bool DrawdownPassed,
    bool ProfitableWindowsPassed)
{
    public bool IsAccepted => TradeReductionPassed && CostReductionPassed &&
        MinimumTradesPassed && ProfitFactorPassed && PositiveNetReturnPassed &&
        BenchmarkExcessPassed && DrawdownPassed && ProfitableWindowsPassed;
}

public static class AdxRegimeValidationAcceptanceEvaluator
{
    public static AdxRegimeValidationAcceptance Evaluate(
        decimal tradeReductionPercent,
        decimal costReductionPercent,
        int completedTrades,
        decimal grossProfit,
        decimal grossLoss,
        decimal compoundedNetReturnPercent,
        decimal benchmarkExcessPercent,
        decimal worstDrawdownPercent,
        decimal profitableWindowPercent)
    {
        if (completedTrades < 0 || grossProfit < 0m || grossLoss < 0m ||
            worstDrawdownPercent is < 0m or > 100m ||
            profitableWindowPercent is < 0m or > 100m)
        {
            throw new DomainRuleViolationException(
                "ADX regime acceptance metrics are invalid.");
        }

        var profitFactorPassed = grossProfit > 0m &&
            (grossLoss == 0m || checked(grossProfit / grossLoss) >= 1.10m);
        return new AdxRegimeValidationAcceptance(
            tradeReductionPercent >= 20m,
            costReductionPercent >= 20m,
            completedTrades >= 30,
            profitFactorPassed,
            compoundedNetReturnPercent > 0m,
            benchmarkExcessPercent >= 0m,
            worstDrawdownPercent <= 5m,
            profitableWindowPercent >= 60m);
    }
}

public sealed record AdxRegimeValidationReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    IReadOnlyList<AdxRegimeValidationWindow> Windows,
    int BaselineCompletedTradeCount,
    int CandidateCompletedTradeCount,
    decimal TradeReductionPercent,
    decimal BaselineTotalExecutionCost,
    decimal CandidateTotalExecutionCost,
    decimal CostReductionPercent,
    decimal CandidateGrossProfit,
    decimal CandidateGrossLoss,
    decimal? CandidateProfitFactor,
    decimal CandidateCompoundedNetReturnPercent,
    decimal BenchmarkCompoundedNetReturnPercent,
    decimal CandidateBenchmarkExcessPercent,
    decimal CandidateWorstDrawdownPercent,
    decimal CandidateProfitableWindowPercent,
    AdxRegimeValidationAcceptance Acceptance);

public static class AdxRegimeValidationReportFactory
{
    public const string SchemaVersion = "adx-regime-validation-v1";

    public static AdxRegimeValidationReport Create(
        WalkForwardSchedule schedule,
        IEnumerable<AdxRegimeValidationWindow> results)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(results);
        var windows = results.ToArray();
        if (windows.Length == 0 || windows.Length != schedule.Windows.Count)
        {
            throw Invalid("requires one result for every schedule window");
        }

        for (var index = 0; index < windows.Length; index++)
        {
            var expected = schedule.Windows[index];
            var actual = windows[index];
            if (actual.Index != expected.Index ||
                actual.BaselineManifest.Split != expected.Split ||
                actual.CandidateManifest.Split != expected.Split ||
                actual.BaselineManifest.StrategyVersion != 2 ||
                actual.CandidateManifest.StrategyVersion != 4 ||
                actual.BaselineManifest.StrategyId != actual.CandidateManifest.StrategyId ||
                actual.Baseline.Trades.Count != actual.Baseline.Execution.CompletedTradeCount ||
                actual.Candidate.Trades.Count != actual.Candidate.Execution.CompletedTradeCount ||
                actual.BaselineManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
                actual.CandidateManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
                actual.Benchmark.EntryAt != expected.Split.TrainEndExclusive ||
                actual.Benchmark.ExitAt != expected.Split.ValidationEndExclusive)
            {
                throw Invalid("contains an inconsistent train/validation window");
            }
        }

        try
        {
            var baselineTrades = windows.Aggregate(0, static (sum, window) =>
            checked(sum + window.Baseline.Execution.CompletedTradeCount));
            var candidateTrades = windows.Aggregate(0, static (sum, window) =>
                checked(sum + window.Candidate.Execution.CompletedTradeCount));
            var baselineCost = windows.Aggregate(0m, static (sum, window) =>
                checked(sum + TotalCost(window.Baseline.Execution)));
            var candidateCost = windows.Aggregate(0m, static (sum, window) =>
                checked(sum + TotalCost(window.Candidate.Execution)));
            var candidateTradeRows = windows.SelectMany(static window => window.Candidate.Trades).ToArray();
            var grossProfit = candidateTradeRows.Where(static trade => trade.NetPnl > 0m)
                .Aggregate(0m, static (sum, trade) => checked(sum + trade.NetPnl));
            var grossLoss = -candidateTradeRows.Where(static trade => trade.NetPnl < 0m)
                .Aggregate(0m, static (sum, trade) => checked(sum + trade.NetPnl));
            decimal? profitFactor = grossLoss == 0m ? null : checked(grossProfit / grossLoss);
            var tradeReduction = Reduction(baselineTrades, candidateTrades);
            var costReduction = Reduction(baselineCost, candidateCost);
            var candidateReturn = Compound(windows.Select(
                static window => window.Candidate.Execution.NetReturnPercent));
            var benchmarkReturn = Compound(windows.Select(
                static window => window.Benchmark.NetReturnPercent));
            var excess = checked(candidateReturn - benchmarkReturn);
            var worstDrawdown = windows.Max(
                static window => window.Candidate.Execution.MaximumDrawdownPercent);
            var profitableWindowPercent = Rate(
                windows.Count(static window => window.Candidate.Execution.NetReturnPercent > 0m),
                windows.Length);
            var acceptance = AdxRegimeValidationAcceptanceEvaluator.Evaluate(
                tradeReduction, costReduction, candidateTrades, grossProfit, grossLoss,
                candidateReturn, excess, worstDrawdown, profitableWindowPercent);
            var runHash = Hash(new
            {
                SchemaVersion,
                Baseline = windows.Select(static window => window.BaselineManifest.ManifestSha256),
                Candidate = windows.Select(static window => window.CandidateManifest.ManifestSha256)
            });
            var reportHash = Hash(new
            {
                SchemaVersion,
                RunSha256 = runHash,
                Windows = windows.Select(static window => new
                {
                    window.Index,
                    Baseline = window.Baseline.ReportSha256,
                    Candidate = window.Candidate.ReportSha256,
                    window.Benchmark
                })
            });

            return new AdxRegimeValidationReport(
                SchemaVersion, runHash, reportHash, Array.AsReadOnly(windows), baselineTrades,
                candidateTrades, tradeReduction, baselineCost, candidateCost, costReduction,
                grossProfit, grossLoss, profitFactor, candidateReturn, benchmarkReturn, excess,
                worstDrawdown, profitableWindowPercent, acceptance);
        }
        catch (OverflowException)
        {
            throw Invalid("aggregate arithmetic exceeded decimal bounds");
        }
    }

    private static decimal TotalCost(BacktestExecutionReport report) =>
        checked(report.TotalFees + report.EstimatedSpreadCost + report.EstimatedSlippageCost);

    private static decimal Reduction(int baseline, int candidate) =>
        baseline <= 0 ? 0m : checked(((decimal)baseline - candidate) / baseline * 100m);

    private static decimal Reduction(decimal baseline, decimal candidate) =>
        baseline <= 0m ? 0m : checked((baseline - candidate) / baseline * 100m);

    private static decimal Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0m : checked((decimal)numerator / denominator * 100m);

    private static decimal Compound(IEnumerable<decimal> returns) =>
        checked((returns.Aggregate(1m, static (factor, value) =>
            checked(factor * checked(1m + value / 100m))) - 1m) * 100m);

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static DomainRuleViolationException Invalid(string reason) =>
        new($"ADX regime validation {reason}.");
}

public sealed class AdxRegimeValidationOrchestrator(
    IHistoricalCandleDatasetFactory datasets,
    DeterministicStrategyBacktest strategyBacktest,
    BacktestExecutionSimulator executionSimulator,
    BuyAndHoldBenchmark benchmark)
{
    public async Task<AdxRegimeValidationReport> RunAsync(
        StrategyDefinition baseline,
        StrategyDefinition candidate,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        int randomSeed,
        BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        ValidateDefinitions(baseline, candidate);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(diagnosticsPolicy);
        var results = new List<AdxRegimeValidationWindow>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            var baselineRun = await RunDefinitionAsync(
                baseline, executionPolicy, window, randomSeed, diagnosticsPolicy, cancellationToken);
            var candidateRun = await RunDefinitionAsync(
                candidate, executionPolicy, window, randomSeed, diagnosticsPolicy, cancellationToken);
            await using var benchmarkDataset = await datasets.OpenAsync(
                baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken);
            var benchmarkReport = await benchmark.RunRangeAsync(
                benchmarkDataset.ReadAsync(cancellationToken),
                window.Split.TrainEndExclusive,
                window.Split.ValidationEndExclusive,
                executionPolicy, baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken);
            results.Add(new AdxRegimeValidationWindow(
                window.Index, baselineRun.Manifest, baselineRun.Diagnostics,
                candidateRun.Manifest, candidateRun.Diagnostics, benchmarkReport));
        }

        return AdxRegimeValidationReportFactory.Create(schedule, results);
    }

    private async Task<DefinitionRun> RunDefinitionAsync(
        StrategyDefinition definition, BacktestExecutionPolicy policy,
        WalkForwardWindow window, int randomSeed, BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        await using var signal = await datasets.OpenAsync(
            definition.InstrumentId, definition.SignalTimeframe, cancellationToken);
        await using var trend = await datasets.OpenAsync(
            definition.InstrumentId, definition.TrendTimeframe, cancellationToken);
        var split = window.Split;
        var decisions = strategyBacktest.RunAsync(
            definition,
            BacktestEvaluationCandleStream.ReadAsync(signal.ReadAsync(cancellationToken),
                split.StartInclusive, split.ValidationEndExclusive, cancellationToken),
            BacktestEvaluationCandleStream.ReadAsync(trend.ReadAsync(cancellationToken),
                split.StartInclusive, split.ValidationEndExclusive, cancellationToken),
            split.TrainEndExclusive,
            cancellationToken);
        var diagnostics = await executionSimulator.RunWithDiagnosticsAsync(
            definition, decisions, policy, diagnosticsPolicy, cancellationToken);
        var manifest = BacktestRunManifestFactory.Create(
            definition, policy, signal.Descriptor, signal.CompletedSummary,
            trend.Descriptor, trend.CompletedSummary, split,
            BacktestExperimentPlan.Create(BacktestRunPurpose.ParameterSelection,
                BacktestDatasetPartition.Train, BacktestDatasetPartition.Validation),
            randomSeed);
        return new DefinitionRun(manifest, diagnostics);
    }

    private static void ValidateDefinitions(
        StrategyDefinition baseline, StrategyDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.Version != 2 || candidate.Version != 4 ||
            baseline.StrategyId != candidate.StrategyId ||
            baseline.InstrumentId != candidate.InstrumentId ||
            baseline.SignalTimeframe != candidate.SignalTimeframe ||
            baseline.TrendTimeframe != candidate.TrendTimeframe ||
            baseline.SignalEmaHysteresisBasisPoints != 30m ||
            candidate.SignalEmaHysteresisBasisPoints != 30m ||
            candidate.TrendStrengthPeriod != 14 || candidate.MinimumTrendStrength != 25m ||
            candidate.ReentryCooldownCandles != 0 ||
            candidate.ProfitProtectionActivationBasisPoints != 0m ||
            candidate.ProfitProtectionTrailingBasisPoints != 0m)
        {
            throw new DomainRuleViolationException(
                "ADX regime validation requires the locked compatible v2 and v4 definitions.");
        }
    }

    private sealed record DefinitionRun(
        BacktestRunManifest Manifest,
        BacktestExecutionDiagnosticsReport Diagnostics);
}
