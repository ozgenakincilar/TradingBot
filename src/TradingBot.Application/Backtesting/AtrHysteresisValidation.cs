using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record AtrHysteresisValidationAcceptance(
    bool MinimumTradesPassed,
    bool ProfitFactorPassed,
    bool PositiveNetReturnPassed,
    bool BenchmarkExcessPassed,
    bool DrawdownPassed,
    bool ProfitableWindowsPassed,
    bool ExecutionCostCoveragePassed,
    bool FullyExecutedPassed)
{
    public bool IsAccepted => MinimumTradesPassed && ProfitFactorPassed &&
        PositiveNetReturnPassed && BenchmarkExcessPassed && DrawdownPassed &&
        ProfitableWindowsPassed && ExecutionCostCoveragePassed && FullyExecutedPassed;
}

public static class AtrHysteresisValidationAcceptanceEvaluator
{
    public static AtrHysteresisValidationAcceptance Evaluate(
        int completedTrades,
        decimal baselineProfitFactorScore,
        decimal candidateProfitFactorScore,
        decimal compoundedNetReturnPercent,
        decimal benchmarkExcessPercent,
        decimal worstDrawdownPercent,
        decimal profitableWindowPercent,
        decimal totalExecutionCost,
        decimal grossBeforeCostProfit,
        bool fullyExecuted)
    {
        if (completedTrades < 0 || baselineProfitFactorScore < 0m ||
            candidateProfitFactorScore < 0m ||
            worstDrawdownPercent is < 0m or > 100m ||
            profitableWindowPercent is < 0m or > 100m ||
            totalExecutionCost < 0m || grossBeforeCostProfit < 0m)
        {
            throw new DomainRuleViolationException(
                "ATR hysteresis acceptance metrics are invalid.");
        }

        return new AtrHysteresisValidationAcceptance(
            completedTrades >= 30,
            candidateProfitFactorScore >= 1.10m &&
                candidateProfitFactorScore > baselineProfitFactorScore,
            compoundedNetReturnPercent > 0m,
            benchmarkExcessPercent >= 0m,
            worstDrawdownPercent <= 5m,
            profitableWindowPercent >= 60m,
            grossBeforeCostProfit > 0m && totalExecutionCost < grossBeforeCostProfit,
            fullyExecuted);
    }
}

public sealed record AtrHysteresisValidationReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    WalkForwardReport Baseline,
    AdaptiveWalkForwardReport Candidate,
    int CandidateCompletedTradeCount,
    decimal? BaselineProfitFactor,
    decimal? CandidateProfitFactor,
    decimal CandidateCompoundedNetReturnPercent,
    decimal BenchmarkCompoundedNetReturnPercent,
    decimal CandidateBenchmarkExcessPercent,
    decimal CandidateWorstDrawdownPercent,
    decimal CandidateProfitableWindowPercent,
    decimal CandidateTotalExecutionCost,
    decimal CandidateGrossBeforeCostProfit,
    AtrHysteresisValidationAcceptance Acceptance);

public sealed class AtrHysteresisValidationOrchestrator(
    IHistoricalCandleDatasetFactory datasets,
    DeterministicStrategyBacktest strategyBacktest,
    BacktestExecutionSimulator executionSimulator,
    BuyAndHoldBenchmark benchmark)
{
    public const string SchemaVersion = "atr-hysteresis-validation-v1";
    public const int MinimumForwardWindowCount = 5;
    public static readonly TimeSpan RequiredForwardWindowDuration = TimeSpan.FromDays(30);
    public static readonly DateTimeOffset EarliestForwardData =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    public async Task<AtrHysteresisValidationReport> RunAsync(
        StrategyDefinition baseline,
        StrategyDefinition candidate,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        AtrHysteresisParameterGrid parameterGrid,
        int randomSeed,
        CancellationToken cancellationToken)
    {
        ValidateInputs(baseline, candidate, executionPolicy, schedule, parameterGrid);
        var orchestrator = new WalkForwardBacktestOrchestrator(
            datasets,
            strategyBacktest,
            executionSimulator,
            benchmark);
        var baselineReport = await orchestrator.RunAsync(
            baseline,
            executionPolicy,
            schedule,
            randomSeed,
            cancellationToken);
        var candidateReport = await orchestrator.RunAdaptiveAsync(
            candidate,
            executionPolicy,
            schedule,
            parameterGrid,
            randomSeed,
            cancellationToken);
        return CreateReport(baselineReport, candidateReport);
    }

    public static AtrHysteresisValidationReport CreateReport(
        WalkForwardReport baseline,
        AdaptiveWalkForwardReport candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        var candidateOos = candidate.OutOfSampleReport;
        if (baseline.StrategyVersion != 5 || candidateOos.StrategyVersion != 6 ||
            baseline.StrategyId != candidateOos.StrategyId ||
            baseline.Windows.Count == 0 ||
            baseline.Windows.Count != candidateOos.Windows.Count ||
            candidate.Selections.Count != candidateOos.Windows.Count)
        {
            throw Invalid("contains inconsistent v5-v6 walk-forward reports");
        }

        try
        {
            var baselineProfit = 0m;
            var baselineLoss = 0m;
            var candidateProfit = 0m;
            var candidateLoss = 0m;
            var candidateCost = 0m;
            var grossBeforeCostProfit = 0m;
            var benchmarkFactor = 1m;
            var worstDrawdown = 0m;
            var profitableWindows = 0;
            var fullyExecuted = true;
            for (var index = 0; index < baseline.Windows.Count; index++)
            {
                var baselineExecution = baseline.Windows[index].Execution;
                var candidateWindow = candidateOos.Windows[index];
                var candidateExecution = candidateWindow.Execution;
                baselineProfit = checked(baselineProfit + baselineExecution.GrossProfit);
                baselineLoss = checked(baselineLoss + baselineExecution.GrossLoss);
                candidateProfit = checked(candidateProfit + candidateExecution.GrossProfit);
                candidateLoss = checked(candidateLoss + candidateExecution.GrossLoss);
                candidateCost = checked(candidateCost + candidateExecution.TotalFees +
                    candidateExecution.EstimatedSpreadCost +
                    candidateExecution.EstimatedSlippageCost);
                var grossPnl = checked(candidateExecution.InitialQuoteBalance *
                    candidateExecution.GrossReturnPercent / 100m);
                if (grossPnl > 0m)
                {
                    grossBeforeCostProfit = checked(grossBeforeCostProfit + grossPnl);
                }

                benchmarkFactor = checked(benchmarkFactor *
                    checked(1m + candidateWindow.Benchmark.NetReturnPercent / 100m));
                worstDrawdown = Math.Max(
                    worstDrawdown,
                    candidateExecution.MaximumDrawdownPercent);
                if (candidateExecution.NetReturnPercent > 0m)
                {
                    profitableWindows = checked(profitableWindows + 1);
                }

                fullyExecuted &= !candidateExecution.HasPendingExecution &&
                    candidateExecution.OpenQuantity == 0m;
            }

            var baselineScore = ProfitFactorScore(baselineProfit, baselineLoss);
            var candidateScore = ProfitFactorScore(candidateProfit, candidateLoss);
            decimal? baselineProfitFactor = baselineLoss == 0m
                ? null
                : checked(baselineProfit / baselineLoss);
            decimal? candidateProfitFactor = candidateLoss == 0m
                ? null
                : checked(candidateProfit / candidateLoss);
            var benchmarkReturn = checked((benchmarkFactor - 1m) * 100m);
            var candidateReturn = candidateOos.CompoundedNetReturnPercent;
            var benchmarkExcess = checked(candidateReturn - benchmarkReturn);
            var profitablePercent = checked(
                (decimal)profitableWindows / candidateOos.Windows.Count * 100m);
            var acceptance = AtrHysteresisValidationAcceptanceEvaluator.Evaluate(
                candidateOos.TotalCompletedTradeCount,
                baselineScore,
                candidateScore,
                candidateReturn,
                benchmarkExcess,
                worstDrawdown,
                profitablePercent,
                candidateCost,
                grossBeforeCostProfit,
                fullyExecuted);
            var runHash = Hash(new
            {
                SchemaVersion,
                Baseline = baseline.RunSha256,
                Candidate = candidateOos.RunSha256,
                candidate.Selections
            });
            var reportHash = Hash(new
            {
                SchemaVersion,
                RunSha256 = runHash,
                Baseline = baseline.ReportSha256,
                Candidate = candidateOos.ReportSha256,
                candidate.Selections,
                Acceptance = acceptance
            });
            return new AtrHysteresisValidationReport(
                SchemaVersion,
                runHash,
                reportHash,
                baseline,
                candidate,
                candidateOos.TotalCompletedTradeCount,
                baselineProfitFactor,
                candidateProfitFactor,
                candidateReturn,
                benchmarkReturn,
                benchmarkExcess,
                worstDrawdown,
                profitablePercent,
                candidateCost,
                grossBeforeCostProfit,
                acceptance);
        }
        catch (OverflowException)
        {
            throw Invalid("aggregate arithmetic exceeded decimal bounds");
        }
    }

    private static decimal ProfitFactorScore(decimal profit, decimal loss) =>
        loss == 0m ? profit > 0m ? decimal.MaxValue : 0m : checked(profit / loss);

    private static void ValidateInputs(
        StrategyDefinition baseline,
        StrategyDefinition candidate,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        AtrHysteresisParameterGrid parameterGrid)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(executionPolicy);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(parameterGrid);
        if (baseline.Version != 5 || candidate.Version != 6 ||
            baseline.StrategyId != candidate.StrategyId ||
            baseline.InstrumentId != candidate.InstrumentId ||
            baseline.SignalTimeframe != candidate.SignalTimeframe ||
            baseline.TrendTimeframe != candidate.TrendTimeframe ||
            baseline.SignalEmaHysteresisBasisPoints != 30m ||
            candidate.SignalEmaHysteresisBasisPoints != 0m ||
            baseline.TrendStrengthPeriod != candidate.TrendStrengthPeriod ||
            baseline.MinimumTrendStrength != candidate.MinimumTrendStrength ||
            !baseline.RequirePositiveDirectionalMovement ||
            !candidate.RequirePositiveDirectionalMovement ||
            executionPolicy.DynamicExecution is null)
        {
            throw Invalid("requires compatible v5-v6 definitions and dynamic execution");
        }

        if (schedule.Windows.Count < MinimumForwardWindowCount ||
            schedule.OutOfSampleDuration != RequiredForwardWindowDuration ||
            schedule.ValidationDuration != RequiredForwardWindowDuration ||
            schedule.Windows[0].Split.StartInclusive < EarliestForwardData)
        {
            throw Invalid(
                "requires at least five post-2026-07-27 forward windows with 30-day validation and OOS durations");
        }
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static DomainRuleViolationException Invalid(string reason) =>
        new($"ATR hysteresis validation {reason}.");
}
