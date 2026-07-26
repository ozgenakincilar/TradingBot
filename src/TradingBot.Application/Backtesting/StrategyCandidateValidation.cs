using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record StrategyValidationWindowResult(
    int Index,
    BacktestRunManifest BaselineManifest,
    BacktestExecutionReport Baseline,
    BacktestRunManifest CandidateManifest,
    BacktestExecutionReport Candidate,
    BuyAndHoldBenchmarkReport Benchmark);

public sealed record StrategyValidationAcceptance(
    bool TradeReductionPassed,
    bool CostReductionPassed,
    bool PositiveNetReturnPassed,
    bool BenchmarkExcessPassed,
    bool DrawdownPassed,
    bool ProfitableWindowsPassed)
{
    public bool IsAccepted => TradeReductionPassed && CostReductionPassed &&
        PositiveNetReturnPassed && BenchmarkExcessPassed && DrawdownPassed &&
        ProfitableWindowsPassed;
}

public sealed record StrategyValidationReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    string StrategyId,
    int BaselineVersion,
    int CandidateVersion,
    IReadOnlyList<StrategyValidationWindowResult> Windows,
    int BaselineCompletedTradeCount,
    int CandidateCompletedTradeCount,
    decimal TradeReductionPercent,
    decimal BaselineTotalExecutionCost,
    decimal CandidateTotalExecutionCost,
    decimal CostReductionPercent,
    decimal CandidateCompoundedNetReturnPercent,
    decimal BenchmarkCompoundedNetReturnPercent,
    decimal CandidateBenchmarkExcessPercent,
    decimal CandidateWorstDrawdownPercent,
    decimal CandidateProfitableWindowPercent,
    StrategyValidationAcceptance Acceptance);

public static class StrategyCandidateValidationReportFactory
{
    public const string SchemaVersion = "strategy-validation-report-v1";

    public static StrategyValidationReport Create(
        WalkForwardSchedule schedule,
        IEnumerable<StrategyValidationWindowResult> results)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(results);
        var windows = results.ToArray();
        if (windows.Length != schedule.Windows.Count || windows.Length == 0)
        {
            throw new DomainRuleViolationException(
                "Strategy validation requires one result for every schedule window.");
        }

        for (var index = 0; index < windows.Length; index++)
        {
            var expected = schedule.Windows[index];
            var actual = windows[index];
            if (actual.Index != expected.Index ||
                actual.BaselineManifest.Split != expected.Split ||
                actual.CandidateManifest.Split != expected.Split ||
                actual.BaselineManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
                actual.CandidateManifest.Purpose != BacktestRunPurpose.ParameterSelection ||
                actual.BaselineManifest.Partitions.ToArray() is not
                    [BacktestDatasetPartition.Train, BacktestDatasetPartition.Validation] ||
                actual.CandidateManifest.Partitions.ToArray() is not
                    [BacktestDatasetPartition.Train, BacktestDatasetPartition.Validation] ||
                actual.Benchmark.EntryAt != expected.Split.TrainEndExclusive ||
                actual.Benchmark.ExitAt != expected.Split.ValidationEndExclusive)
            {
                throw new DomainRuleViolationException(
                    "Strategy validation window does not match its train/validation schedule.");
            }


            ValidateExecution(actual.Baseline);
            ValidateExecution(actual.Candidate);
            ValidateBenchmark(actual.Benchmark, actual.Baseline.InitialQuoteBalance);
            if (actual.Candidate.InitialQuoteBalance != actual.Baseline.InitialQuoteBalance)
            {
                throw new DomainRuleViolationException(
                    "Strategy validation capital must match across both versions.");
            }
        }

        var first = windows[0];
        if (windows.Any(window =>
                window.BaselineManifest.StrategyId != first.BaselineManifest.StrategyId ||
                window.BaselineManifest.StrategyVersion != first.BaselineManifest.StrategyVersion ||
                window.CandidateManifest.StrategyId != first.CandidateManifest.StrategyId ||
                window.CandidateManifest.StrategyVersion != first.CandidateManifest.StrategyVersion) ||
            first.BaselineManifest.StrategyId != first.CandidateManifest.StrategyId ||
            first.CandidateManifest.StrategyVersion <= first.BaselineManifest.StrategyVersion)
        {
            throw new DomainRuleViolationException(
                "Strategy validation identities and versions are inconsistent.");
        }

        var baselineTrades = windows.Aggregate(0,
            static (sum, window) => Add(sum, window.Baseline.CompletedTradeCount));
        var candidateTrades = windows.Aggregate(0,
            static (sum, window) => Add(sum, window.Candidate.CompletedTradeCount));
        var baselineCost = windows.Aggregate(0m,
            static (sum, window) => Add(sum, TotalCost(window.Baseline)));
        var candidateCost = windows.Aggregate(0m,
            static (sum, window) => Add(sum, TotalCost(window.Candidate)));
        var tradeReduction = Reduction(baselineTrades, candidateTrades);
        var costReduction = Reduction(baselineCost, candidateCost);
        var candidateFactor = windows.Aggregate(1m,
            static (factor, window) => Multiply(
                factor, Add(1m, window.Candidate.NetReturnPercent / 100m)));
        var benchmarkFactor = windows.Aggregate(1m,
            static (factor, window) => Multiply(
                factor, Add(1m, window.Benchmark.NetReturnPercent / 100m)));
        var candidateCompounded = Multiply(Add(candidateFactor, -1m), 100m);
        var benchmarkCompounded = Multiply(Add(benchmarkFactor, -1m), 100m);
        var excess = Add(candidateCompounded, -benchmarkCompounded);
        var worstDrawdown = windows.Max(static window => window.Candidate.MaximumDrawdownPercent);
        var profitablePercent = (decimal)windows.Count(
            static window => window.Candidate.NetReturnPercent > 0m) / windows.Length * 100m;
        var acceptance = new StrategyValidationAcceptance(
            tradeReduction >= 30m,
            costReduction >= 30m,
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
                window.Baseline,
                window.Candidate,
                window.Benchmark
            }).ToArray()
        });

        return new StrategyValidationReport(
            SchemaVersion,
            runHash,
            reportHash,
            first.BaselineManifest.StrategyId,
            first.BaselineManifest.StrategyVersion,
            first.CandidateManifest.StrategyVersion,
            Array.AsReadOnly(windows),
            baselineTrades,
            candidateTrades,
            tradeReduction,
            baselineCost,
            candidateCost,
            costReduction,
            candidateCompounded,
            benchmarkCompounded,
            excess,
            worstDrawdown,
            profitablePercent,
            acceptance);
    }

    private static decimal TotalCost(BacktestExecutionReport report) =>
        Add(Add(report.TotalFees, report.EstimatedSpreadCost), report.EstimatedSlippageCost);

    private static decimal Reduction(decimal baseline, decimal candidate) =>
        baseline <= 0m ? 0m : Multiply((baseline - candidate) / baseline, 100m);

    private static decimal Reduction(int baseline, int candidate) =>
        baseline <= 0 ? 0m : ((decimal)baseline - candidate) / baseline * 100m;

    private static void ValidateExecution(BacktestExecutionReport report)
    {
        decimal expectedNet;
        decimal expectedGross;
        try
        {
            expectedNet = ((report.NetLiquidationValue - report.InitialQuoteBalance) /
                report.InitialQuoteBalance) * 100m;
            expectedGross = ((report.NetLiquidationValue + report.TotalFees +
                report.EstimatedSpreadCost + report.EstimatedSlippageCost -
                report.InitialQuoteBalance) / report.InitialQuoteBalance) * 100m;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            throw new DomainRuleViolationException(
                "Strategy validation execution return is invalid.");
        }

        if (report.InitialQuoteBalance <= 0m || report.EndingCashBalance < 0m ||
            report.OpenQuantity < 0m || report.NetLiquidationValue < 0m ||
            report.NetReturnPercent != expectedNet || report.GrossReturnPercent != expectedGross ||
            report.TotalFees < 0m || report.EstimatedSpreadCost < 0m ||
            report.EstimatedSlippageCost < 0m ||
            report.MaximumDrawdownPercent is < 0m or > 100m ||
            report.FillCount < 0 || report.CompletedTradeCount < 0 ||
            report.WinningTradeCount < 0 ||
            report.WinningTradeCount > report.CompletedTradeCount)
        {
            throw new DomainRuleViolationException(
                "Strategy validation execution report is invalid.");
        }
    }

    private static void ValidateBenchmark(
        BuyAndHoldBenchmarkReport report,
        decimal expectedInitialBalance)
    {
        decimal expectedNet;
        try
        {
            expectedNet = ((report.NetLiquidationValue - report.InitialQuoteBalance) /
                report.InitialQuoteBalance) * 100m;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            throw new DomainRuleViolationException(
                "Strategy validation benchmark return is invalid.");
        }

        if (report.InitialQuoteBalance != expectedInitialBalance ||
            report.InitialQuoteBalance <= 0m || report.AllocatedQuoteBalance <= 0m ||
            report.EndingCashBalance != report.InitialQuoteBalance - report.AllocatedQuoteBalance ||
            report.BaseQuantity <= 0m || report.NetLiquidationValue < 0m ||
            report.NetReturnPercent != expectedNet || report.TotalFees < 0m ||
            report.EstimatedSpreadCost < 0m || report.EstimatedSlippageCost < 0m ||
            report.MaximumDrawdownPercent is < 0m or > 100m || report.CandleCount <= 0)
        {
            throw new DomainRuleViolationException(
                "Strategy validation benchmark report is invalid.");
        }
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

    private static DomainRuleViolationException Overflow() => new(
        "Strategy validation aggregate metric overflowed.");
}
