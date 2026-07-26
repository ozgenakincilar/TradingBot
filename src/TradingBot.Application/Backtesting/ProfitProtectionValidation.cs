using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record ProfitProtectionValidationWindow(
    int Index,
    BacktestRunManifest BaselineManifest,
    BacktestExecutionDiagnosticsReport Baseline,
    BacktestRunManifest CandidateManifest,
    BacktestExecutionDiagnosticsReport Candidate,
    BuyAndHoldBenchmarkReport Benchmark);

public sealed record ProfitProtectionValidationAcceptance(
    bool TradeReductionPassed,
    bool CostReductionPassed,
    bool FavorableGivebackReductionPassed,
    bool PositiveNetReturnPassed,
    bool BenchmarkExcessPassed,
    bool DrawdownPassed,
    bool ProfitableWindowsPassed)
{
    public bool IsAccepted => TradeReductionPassed && CostReductionPassed &&
        FavorableGivebackReductionPassed && PositiveNetReturnPassed &&
        BenchmarkExcessPassed && DrawdownPassed && ProfitableWindowsPassed;
}

public sealed record ProfitProtectionValidationReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    string StrategyId,
    int BaselineVersion,
    int CandidateVersion,
    IReadOnlyList<ProfitProtectionValidationWindow> Windows,
    int BaselineCompletedTradeCount,
    int CandidateCompletedTradeCount,
    decimal TradeReductionPercent,
    decimal BaselineTotalExecutionCost,
    decimal CandidateTotalExecutionCost,
    decimal CostReductionPercent,
    decimal BaselineFavorableGivebackRatePercent,
    decimal CandidateFavorableGivebackRatePercent,
    decimal FavorableGivebackRateReductionPercent,
    decimal CandidateCompoundedNetReturnPercent,
    decimal BenchmarkCompoundedNetReturnPercent,
    decimal CandidateBenchmarkExcessPercent,
    decimal CandidateWorstDrawdownPercent,
    decimal CandidateProfitableWindowPercent,
    ProfitProtectionValidationAcceptance Acceptance);

public static class ProfitProtectionValidationReportFactory
{
    public const string SchemaVersion = "profit-protection-validation-v1";

    public static ProfitProtectionValidationReport Create(
        WalkForwardSchedule schedule,
        IEnumerable<ProfitProtectionValidationWindow> results)
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
            ValidateWindow(schedule.Windows[index], windows[index]);
        }

        var first = windows[0];
        if (first.BaselineManifest.StrategyVersion != 2 ||
            first.CandidateManifest.StrategyVersion != 3 ||
            first.BaselineManifest.StrategyId != first.CandidateManifest.StrategyId ||
            windows.Any(window =>
                window.BaselineManifest.StrategyId != first.BaselineManifest.StrategyId ||
                window.CandidateManifest.StrategyId != first.CandidateManifest.StrategyId ||
                window.BaselineManifest.StrategyVersion != 2 ||
                window.CandidateManifest.StrategyVersion != 3))
        {
            throw Invalid("requires consistent v2 and v3 strategy identities");
        }

        var baselineTrades = windows.Aggregate(0,
            static (sum, window) => Add(sum, window.Baseline.Execution.CompletedTradeCount));
        var candidateTrades = windows.Aggregate(0,
            static (sum, window) => Add(sum, window.Candidate.Execution.CompletedTradeCount));
        var baselineCost = windows.Aggregate(0m,
            static (sum, window) => Add(sum, TotalCost(window.Baseline.Execution)));
        var candidateCost = windows.Aggregate(0m,
            static (sum, window) => Add(sum, TotalCost(window.Candidate.Execution)));
        var baselineGivebacks = windows.Aggregate(0,
            static (sum, window) => Add(
                sum, window.Baseline.FavorableExcursionGivenBackTradeCount));
        var candidateGivebacks = windows.Aggregate(0,
            static (sum, window) => Add(
                sum, window.Candidate.FavorableExcursionGivenBackTradeCount));
        var baselineGivebackRate = Rate(baselineGivebacks, baselineTrades);
        var candidateGivebackRate = Rate(candidateGivebacks, candidateTrades);
        var tradeReduction = Reduction(baselineTrades, candidateTrades);
        var costReduction = Reduction(baselineCost, candidateCost);
        var givebackReduction = Reduction(baselineGivebackRate, candidateGivebackRate);
        var candidateCompounded = Compound(windows.Select(
            static window => window.Candidate.Execution.NetReturnPercent));
        var benchmarkCompounded = Compound(windows.Select(
            static window => window.Benchmark.NetReturnPercent));
        var excess = Add(candidateCompounded, -benchmarkCompounded);
        var worstDrawdown = windows.Max(
            static window => window.Candidate.Execution.MaximumDrawdownPercent);
        var profitablePercent = Rate(
            windows.Count(static window => window.Candidate.Execution.NetReturnPercent > 0m),
            windows.Length);
        var acceptance = new ProfitProtectionValidationAcceptance(
            tradeReduction >= 20m,
            costReduction >= 20m,
            givebackReduction >= 30m,
            candidateCompounded > 0m,
            excess >= 0m,
            worstDrawdown <= 5m,
            profitablePercent >= 60m);
        var runHash = Hash(new
        {
            SchemaVersion,
            Baseline = windows.Select(
                static window => window.BaselineManifest.ManifestSha256).ToArray(),
            Candidate = windows.Select(
                static window => window.CandidateManifest.ManifestSha256).ToArray()
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
            }).ToArray()
        });

        return new ProfitProtectionValidationReport(
            SchemaVersion,
            runHash,
            reportHash,
            first.BaselineManifest.StrategyId,
            2,
            3,
            Array.AsReadOnly(windows),
            baselineTrades,
            candidateTrades,
            tradeReduction,
            baselineCost,
            candidateCost,
            costReduction,
            baselineGivebackRate,
            candidateGivebackRate,
            givebackReduction,
            candidateCompounded,
            benchmarkCompounded,
            excess,
            worstDrawdown,
            profitablePercent,
            acceptance);
    }

    private static void ValidateWindow(
        WalkForwardWindow expected,
        ProfitProtectionValidationWindow actual)
    {
        var expectedPartitions = new[]
        {
            BacktestDatasetPartition.Train,
            BacktestDatasetPartition.Validation
        };
        if (actual.Index != expected.Index ||
            actual.BaselineManifest.Split != expected.Split ||
            actual.CandidateManifest.Split != expected.Split ||
            actual.BaselineManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
            actual.CandidateManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
            !actual.BaselineManifest.Partitions.SequenceEqual(expectedPartitions) ||
            !actual.CandidateManifest.Partitions.SequenceEqual(expectedPartitions) ||
            actual.Benchmark.EntryAt != expected.Split.TrainEndExclusive ||
            actual.Benchmark.ExitAt != expected.Split.ValidationEndExclusive ||
            actual.Baseline.Execution.InitialQuoteBalance !=
                actual.Candidate.Execution.InitialQuoteBalance ||
            actual.Baseline.Execution.InitialQuoteBalance != actual.Benchmark.InitialQuoteBalance)
        {
            throw Invalid("window does not match its train/validation schedule");
        }


        ValidateDiagnostics(actual.Baseline);
        ValidateDiagnostics(actual.Candidate);
        ValidateBenchmark(actual.Benchmark);
    }

    private static void ValidateDiagnostics(BacktestExecutionDiagnosticsReport diagnostics)
    {
        var execution = diagnostics.Execution;
        decimal expectedNet;
        decimal expectedGross;
        try
        {
            expectedNet = (execution.NetLiquidationValue - execution.InitialQuoteBalance) /
                execution.InitialQuoteBalance * 100m;
            expectedGross = (execution.NetLiquidationValue + execution.TotalFees +
                execution.EstimatedSpreadCost + execution.EstimatedSlippageCost -
                execution.InitialQuoteBalance) / execution.InitialQuoteBalance * 100m;
        }
        catch (Exception exception) when (
            exception is OverflowException or DivideByZeroException)
        {
            throw Invalid("contains invalid execution arithmetic");
        }

        var expectedGivebacks = diagnostics.Trades.Count(static trade =>
            trade.MaximumFavorableExcursionPercent > 0m && trade.NetPnl <= 0m);
        if (diagnostics.SchemaVersion != 1 || diagnostics.ReportSha256.Length != 64 ||
            diagnostics.Trades.Count != execution.CompletedTradeCount ||
            diagnostics.FavorableExcursionGivenBackTradeCount != expectedGivebacks ||
            execution.InitialQuoteBalance <= 0m || execution.EndingCashBalance < 0m ||
            execution.OpenQuantity < 0m || execution.NetLiquidationValue < 0m ||
            execution.NetReturnPercent != expectedNet ||
            execution.GrossReturnPercent != expectedGross ||
            execution.TotalFees < 0m || execution.EstimatedSpreadCost < 0m ||
            execution.EstimatedSlippageCost < 0m ||
            execution.MaximumDrawdownPercent is < 0m or > 100m ||
            execution.CompletedTradeCount < 0)
        {
            throw Invalid("contains an invalid diagnostics report");
        }
    }

    private static void ValidateBenchmark(BuyAndHoldBenchmarkReport benchmark)
    {
        decimal expectedNet;
        try
        {
            expectedNet = (benchmark.NetLiquidationValue - benchmark.InitialQuoteBalance) /
                benchmark.InitialQuoteBalance * 100m;
        }
        catch (Exception exception) when (
            exception is OverflowException or DivideByZeroException)
        {
            throw Invalid("contains invalid benchmark arithmetic");
        }

        if (benchmark.InitialQuoteBalance <= 0m || benchmark.NetLiquidationValue < 0m ||
            benchmark.NetReturnPercent != expectedNet || benchmark.TotalFees < 0m ||
            benchmark.EstimatedSpreadCost < 0m || benchmark.EstimatedSlippageCost < 0m ||
            benchmark.MaximumDrawdownPercent is < 0m or > 100m || benchmark.CandleCount <= 0)
        {
            throw Invalid("contains an invalid benchmark report");
        }
    }

    private static decimal TotalCost(BacktestExecutionReport report) => Add(
        Add(report.TotalFees, report.EstimatedSpreadCost),
        report.EstimatedSlippageCost);

    private static decimal Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0m : (decimal)numerator / denominator * 100m;

    private static decimal Reduction(int baseline, int candidate) =>
        baseline <= 0 ? 0m : ((decimal)baseline - candidate) / baseline * 100m;

    private static decimal Reduction(decimal baseline, decimal candidate) =>
        baseline <= 0m ? 0m : Multiply((baseline - candidate) / baseline, 100m);

    private static decimal Compound(IEnumerable<decimal> returns)
    {
        var factor = returns.Aggregate(
            1m,
            static (current, value) => Multiply(current, Add(1m, value / 100m)));
        return Multiply(Add(factor, -1m), 100m);
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static decimal Add(decimal left, decimal right)
    {
        try { return checked(left + right); }
        catch (OverflowException) { throw Overflow(); }
    }

    private static int Add(int left, int right)
    {
        try { return checked(left + right); }
        catch (OverflowException) { throw Overflow(); }
    }

    private static decimal Multiply(decimal left, decimal right)
    {
        try { return checked(left * right); }
        catch (OverflowException) { throw Overflow(); }
    }

    private static DomainRuleViolationException Invalid(string reason) => new(
        $"Profit protection validation {reason}.");

    private static DomainRuleViolationException Overflow() => new(
        "Profit protection validation aggregate metric overflowed.");
}

public sealed class ProfitProtectionValidationOrchestrator
{
    private readonly IHistoricalCandleDatasetFactory _datasets;
    private readonly DeterministicStrategyBacktest _strategyBacktest;
    private readonly BacktestExecutionSimulator _executionSimulator;
    private readonly BuyAndHoldBenchmark _benchmark;

    public ProfitProtectionValidationOrchestrator(
        IHistoricalCandleDatasetFactory datasets,
        DeterministicStrategyBacktest strategyBacktest,
        BacktestExecutionSimulator executionSimulator,
        BuyAndHoldBenchmark benchmark)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(strategyBacktest);
        ArgumentNullException.ThrowIfNull(executionSimulator);
        ArgumentNullException.ThrowIfNull(benchmark);
        _datasets = datasets;
        _strategyBacktest = strategyBacktest;
        _executionSimulator = executionSimulator;
        _benchmark = benchmark;
    }

    public async Task<ProfitProtectionValidationReport> RunAsync(
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
        executionPolicy.Validate(baseline.SignalTimeframe, baseline.InstrumentId);
        diagnosticsPolicy.Validate();
        var results = new List<ProfitProtectionValidationWindow>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            var baselineRun = await RunDefinitionAsync(
                baseline, executionPolicy, window, randomSeed, diagnosticsPolicy,
                cancellationToken);
            var candidateRun = await RunDefinitionAsync(
                candidate, executionPolicy, window, randomSeed, diagnosticsPolicy,
                cancellationToken);
            await using var benchmarkDataset = await _datasets.OpenAsync(
                baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken);
            var benchmark = await _benchmark.RunRangeAsync(
                benchmarkDataset.ReadAsync(cancellationToken),
                window.Split.TrainEndExclusive,
                window.Split.ValidationEndExclusive,
                executionPolicy,
                baseline.InstrumentId,
                baseline.SignalTimeframe,
                cancellationToken);
            results.Add(new ProfitProtectionValidationWindow(
                window.Index,
                baselineRun.Manifest,
                baselineRun.Diagnostics,
                candidateRun.Manifest,
                candidateRun.Diagnostics,
                benchmark));
        }

        return ProfitProtectionValidationReportFactory.Create(schedule, results);
    }

    private async Task<DefinitionRun> RunDefinitionAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy policy,
        WalkForwardWindow window,
        int randomSeed,
        BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        await using var signal = await _datasets.OpenAsync(
            definition.InstrumentId, definition.SignalTimeframe, cancellationToken);
        await using var trend = await _datasets.OpenAsync(
            definition.InstrumentId, definition.TrendTimeframe, cancellationToken);
        var split = window.Split;
        var counter = new DecisionCounter();
        var decisions = _strategyBacktest.RunAsync(
            definition,
            BacktestEvaluationCandleStream.ReadAsync(
                signal.ReadAsync(cancellationToken), split.StartInclusive,
                split.ValidationEndExclusive, cancellationToken),
            BacktestEvaluationCandleStream.ReadAsync(
                trend.ReadAsync(cancellationToken), split.StartInclusive,
                split.ValidationEndExclusive, cancellationToken),
            split.TrainEndExclusive,
            cancellationToken);
        var diagnostics = await _executionSimulator.RunWithDiagnosticsAsync(
            definition,
            CountAsync(decisions, counter, cancellationToken),
            policy,
            diagnosticsPolicy,
            cancellationToken);
        if (counter.Count == 0)
        {
            throw new DomainRuleViolationException(
                "Profit protection validation window produced no decisions.");
        }

        var manifest = BacktestRunManifestFactory.Create(
            definition,
            policy,
            signal.Descriptor,
            signal.CompletedSummary,
            trend.Descriptor,
            trend.CompletedSummary,
            split,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.ParameterSelection,
                BacktestDatasetPartition.Train,
                BacktestDatasetPartition.Validation),
            randomSeed);
        return new DefinitionRun(manifest, diagnostics);
    }

    private static async IAsyncEnumerable<StrategyBacktestDecision> CountAsync(
        IAsyncEnumerable<StrategyBacktestDecision> source,
        DecisionCounter counter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var decision in source.WithCancellation(cancellationToken))
        {
            counter.Increment();
            yield return decision;
        }
    }

    private static void ValidateDefinitions(
        StrategyDefinition baseline,
        StrategyDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.Version != 2 || candidate.Version != 3 ||
            baseline.StrategyId != candidate.StrategyId ||
            baseline.InstrumentId != candidate.InstrumentId ||
            baseline.SignalTimeframe != candidate.SignalTimeframe ||
            baseline.TrendTimeframe != candidate.TrendTimeframe ||
            baseline.SignalEmaHysteresisBasisPoints != 30m ||
            candidate.SignalEmaHysteresisBasisPoints != 30m ||
            candidate.ReentryCooldownCandles != 4 ||
            candidate.ProfitProtectionActivationBasisPoints != 100m ||
            candidate.ProfitProtectionTrailingBasisPoints != 50m)
        {
            throw new DomainRuleViolationException(
                "Profit protection validation requires the locked compatible v2 and v3 definitions.");
        }
    }

    private sealed record DefinitionRun(
        BacktestRunManifest Manifest,
        BacktestExecutionDiagnosticsReport Diagnostics);

    private sealed class DecisionCounter
    {
        public int Count { get; private set; }

        public void Increment()
        {
            try { Count = checked(Count + 1); }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Profit protection validation decision count overflowed.");
            }
        }
    }
}
