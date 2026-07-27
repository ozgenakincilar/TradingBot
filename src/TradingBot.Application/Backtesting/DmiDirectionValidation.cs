using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record DmiDirectionValidationAcceptance(
    bool MinimumTradesPassed,
    bool TradeReductionPassed,
    bool CostReductionPassed,
    bool ProfitFactorPassed,
    bool PositiveNetReturnPassed,
    bool BenchmarkExcessPassed,
    bool DrawdownPassed,
    bool ProfitableWindowsPassed)
{
    public bool IsAccepted => MinimumTradesPassed && TradeReductionPassed &&
        CostReductionPassed && ProfitFactorPassed && PositiveNetReturnPassed &&
        BenchmarkExcessPassed && DrawdownPassed && ProfitableWindowsPassed;
}

public static class DmiDirectionValidationAcceptanceEvaluator
{
    public static DmiDirectionValidationAcceptance Evaluate(
        int baselineTrades, int candidateTrades,
        decimal baselineCost, decimal candidateCost,
        decimal? baselineProfitFactor, decimal? candidateProfitFactor,
        decimal candidateReturn, decimal benchmarkExcess,
        decimal worstDrawdown, decimal profitableWindowPercent) => new(
            candidateTrades >= 30,
            candidateTrades < baselineTrades,
            candidateCost < baselineCost,
            candidateProfitFactor is >= 1.10m &&
                (baselineProfitFactor is null || candidateProfitFactor > baselineProfitFactor),
            candidateReturn > 0m,
            benchmarkExcess >= 0m,
            worstDrawdown <= 5m,
            profitableWindowPercent >= 60m);
}

public sealed record DmiDirectionValidationReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    StrategyLossDiagnosticsReport Baseline,
    StrategyLossDiagnosticsReport Candidate,
    IReadOnlyList<BuyAndHoldBenchmarkReport> Benchmarks,
    decimal BaselineTotalExecutionCost,
    decimal CandidateTotalExecutionCost,
    decimal? BaselineProfitFactor,
    decimal? CandidateProfitFactor,
    decimal CandidateCompoundedNetReturnPercent,
    decimal BenchmarkCompoundedNetReturnPercent,
    decimal CandidateBenchmarkExcessPercent,
    decimal CandidateWorstDrawdownPercent,
    decimal CandidateProfitableWindowPercent,
    DmiDirectionValidationAcceptance Acceptance);

public sealed class DmiDirectionValidationOrchestrator(
    IHistoricalCandleDatasetFactory datasets,
    DeterministicStrategyBacktest strategyBacktest,
    BacktestExecutionSimulator executionSimulator,
    BuyAndHoldBenchmark benchmark)
{
    public const string SchemaVersion = "dmi-direction-validation-v1";

    public async Task<DmiDirectionValidationReport> RunAsync(
        StrategyDefinition baseline,
        StrategyDefinition candidate,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        int randomSeed,
        BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        ValidateDefinitions(baseline, candidate);
        var diagnostics = new StrategyLossDiagnosticsOrchestrator(
            datasets, strategyBacktest, executionSimulator);
        var baselineReport = await diagnostics.RunAsync(
            baseline, executionPolicy, schedule, randomSeed, diagnosticsPolicy,
            cancellationToken);
        var candidateReport = await diagnostics.RunAsync(
            candidate, executionPolicy, schedule, randomSeed, diagnosticsPolicy,
            cancellationToken);
        var benchmarks = new List<BuyAndHoldBenchmarkReport>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            await using var dataset = await datasets.OpenAsync(
                baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken);
            benchmarks.Add(await benchmark.RunRangeAsync(
                dataset.ReadAsync(cancellationToken), window.Split.TrainEndExclusive,
                window.Split.ValidationEndExclusive, executionPolicy,
                baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken));
        }

        return CreateReport(baselineReport, candidateReport, benchmarks);
    }

    private static DmiDirectionValidationReport CreateReport(
        StrategyLossDiagnosticsReport baseline,
        StrategyLossDiagnosticsReport candidate,
        IReadOnlyList<BuyAndHoldBenchmarkReport> benchmarks)
    {
        if (baseline.StrategyVersion != 4 || candidate.StrategyVersion != 5 ||
            baseline.StrategyId != candidate.StrategyId ||
            baseline.Windows.Count == 0 ||
            baseline.Windows.Count != candidate.Windows.Count ||
            baseline.Windows.Count != benchmarks.Count)
        {
            throw Invalid("contains inconsistent v4-v5 reports");
        }

        try
        {
            var baselineCost = baseline.ExitReasonSummaries.Aggregate(0m,
                static (sum, reason) => checked(sum + reason.TotalEstimatedExecutionCost));
            var candidateCost = candidate.ExitReasonSummaries.Aggregate(0m,
                static (sum, reason) => checked(sum + reason.TotalEstimatedExecutionCost));
            var baselineProfitFactor = ProfitFactor(baseline);
            var candidateProfitFactor = ProfitFactor(candidate);
            var candidateReturn = Compound(candidate.Windows.Select(
                static window => window.Diagnostics.Execution.NetReturnPercent));
            var benchmarkReturn = Compound(benchmarks.Select(
                static report => report.NetReturnPercent));
            var excess = checked(candidateReturn - benchmarkReturn);
            var worstDrawdown = candidate.Windows.Max(
                static window => window.Diagnostics.Execution.MaximumDrawdownPercent);
            var profitablePercent = Rate(candidate.Windows.Count(static window =>
                window.Diagnostics.Execution.NetReturnPercent > 0m), candidate.Windows.Count);
            var acceptance = DmiDirectionValidationAcceptanceEvaluator.Evaluate(
                baseline.CompletedTradeCount, candidate.CompletedTradeCount,
                baselineCost, candidateCost, baselineProfitFactor, candidateProfitFactor,
                candidateReturn, excess, worstDrawdown, profitablePercent);
            var runHash = Hash(new
            {
                SchemaVersion,
                Baseline = baseline.RunSha256,
                Candidate = candidate.RunSha256
            });
            var reportHash = Hash(new
            {
                SchemaVersion,
                RunSha256 = runHash,
                Baseline = baseline.ReportSha256,
                Candidate = candidate.ReportSha256,
                Benchmarks = benchmarks
            });
            return new DmiDirectionValidationReport(
                SchemaVersion, runHash, reportHash, baseline, candidate,
                benchmarks.ToArray(), baselineCost, candidateCost,
                baselineProfitFactor, candidateProfitFactor, candidateReturn,
                benchmarkReturn, excess, worstDrawdown, profitablePercent, acceptance);
        }
        catch (OverflowException)
        {
            throw Invalid("aggregate arithmetic exceeded decimal bounds");
        }
    }

    private static decimal? ProfitFactor(StrategyLossDiagnosticsReport report)
    {
        var trades = report.Windows.SelectMany(static window => window.Diagnostics.Trades);
        var profit = trades.Where(static trade => trade.NetPnl > 0m)
            .Aggregate(0m, static (sum, trade) => checked(sum + trade.NetPnl));
        var loss = -trades.Where(static trade => trade.NetPnl < 0m)
            .Aggregate(0m, static (sum, trade) => checked(sum + trade.NetPnl));
        return loss == 0m ? null : checked(profit / loss);
    }

    private static decimal Compound(IEnumerable<decimal> returns) =>
        checked((returns.Aggregate(1m, static (factor, value) =>
            checked(factor * checked(1m + value / 100m))) - 1m) * 100m);

    private static decimal Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0m : checked((decimal)numerator / denominator * 100m);

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static void ValidateDefinitions(
        StrategyDefinition baseline, StrategyDefinition candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        if (baseline.Version != 4 || candidate.Version != 5 ||
            baseline.StrategyId != candidate.StrategyId ||
            baseline.InstrumentId != candidate.InstrumentId ||
            baseline.SignalTimeframe != candidate.SignalTimeframe ||
            baseline.TrendTimeframe != candidate.TrendTimeframe ||
            baseline.TrendStrengthPeriod != 14 || candidate.TrendStrengthPeriod != 14 ||
            baseline.MinimumTrendStrength != 25m || candidate.MinimumTrendStrength != 25m ||
            baseline.RequirePositiveDirectionalMovement ||
            !candidate.RequirePositiveDirectionalMovement)
        {
            throw Invalid("requires locked compatible v4 and v5 definitions");
        }
    }

    private static DomainRuleViolationException Invalid(string reason) =>
        new($"DMI direction validation {reason}.");
}
