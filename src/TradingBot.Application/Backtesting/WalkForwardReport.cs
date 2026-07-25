using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;

namespace TradingBot.Application.Backtesting;

public sealed record WalkForwardWindowResult(
    int Index,
    BacktestRunManifest Manifest,
    BacktestExecutionReport Execution);

public sealed record WalkForwardReport
{
    internal WalkForwardReport(
        string schemaVersion,
        string scheduleSha256,
        string runSha256,
        string reportSha256,
        string strategyId,
        int strategyVersion,
        WalkForwardTrainingMode trainingMode,
        TimeSpan trainingDuration,
        TimeSpan validationDuration,
        TimeSpan outOfSampleDuration,
        IReadOnlyList<WalkForwardWindowResult> windows,
        int profitableWindowCount,
        int totalCompletedTradeCount,
        decimal totalFees,
        decimal meanNetReturnPercent,
        decimal medianNetReturnPercent,
        decimal worstNetReturnPercent,
        decimal bestNetReturnPercent,
        decimal compoundedNetReturnPercent,
        decimal meanMaximumDrawdownPercent)
    {
        SchemaVersion = schemaVersion;
        ScheduleSha256 = scheduleSha256;
        RunSha256 = runSha256;
        ReportSha256 = reportSha256;
        StrategyId = strategyId;
        StrategyVersion = strategyVersion;
        TrainingMode = trainingMode;
        TrainingDuration = trainingDuration;
        ValidationDuration = validationDuration;
        OutOfSampleDuration = outOfSampleDuration;
        Windows = windows;
        ProfitableWindowCount = profitableWindowCount;
        TotalCompletedTradeCount = totalCompletedTradeCount;
        TotalFees = totalFees;
        MeanNetReturnPercent = meanNetReturnPercent;
        MedianNetReturnPercent = medianNetReturnPercent;
        WorstNetReturnPercent = worstNetReturnPercent;
        BestNetReturnPercent = bestNetReturnPercent;
        CompoundedNetReturnPercent = compoundedNetReturnPercent;
        MeanMaximumDrawdownPercent = meanMaximumDrawdownPercent;
    }

    public string SchemaVersion { get; }

    public string ScheduleSha256 { get; }

    public string RunSha256 { get; }

    public string ReportSha256 { get; }

    public string StrategyId { get; }

    public int StrategyVersion { get; }

    public WalkForwardTrainingMode TrainingMode { get; }

    public TimeSpan TrainingDuration { get; }

    public TimeSpan ValidationDuration { get; }

    public TimeSpan OutOfSampleDuration { get; }

    public IReadOnlyList<WalkForwardWindowResult> Windows { get; }

    public int ProfitableWindowCount { get; }

    public int TotalCompletedTradeCount { get; }

    public decimal TotalFees { get; }

    public decimal MeanNetReturnPercent { get; }

    public decimal MedianNetReturnPercent { get; }

    public decimal WorstNetReturnPercent { get; }

    public decimal BestNetReturnPercent { get; }

    public decimal CompoundedNetReturnPercent { get; }

    public decimal MeanMaximumDrawdownPercent { get; }
}

public static class WalkForwardReportFactory
{
    public const string SchemaVersion = "walk-forward-report-v1";

    public static WalkForwardReport Create(
        WalkForwardSchedule schedule,
        IEnumerable<WalkForwardWindowResult> results)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(results);
        var materialized = results.ToArray();
        if (materialized.Length != schedule.Windows.Count)
        {
            throw new DomainRuleViolationException(
                "Walk-forward report requires exactly one result for every schedule window.");
        }

        for (var index = 0; index < materialized.Length; index++)
        {
            ValidateWindow(schedule.Windows[index], materialized[index]);
        }

        var firstManifest = materialized[0].Manifest;
        if (materialized.Any(result =>
                result.Manifest.StrategyId != firstManifest.StrategyId ||
                result.Manifest.StrategyVersion != firstManifest.StrategyVersion))
        {
            throw new DomainRuleViolationException(
                "Walk-forward windows must use one strategy identity and version.");
        }

        var scheduleHash = Hash(new
        {
            schedule.TrainingMode,
            TrainingTicks = schedule.TrainingDuration.Ticks,
            ValidationTicks = schedule.ValidationDuration.Ticks,
            OutOfSampleTicks = schedule.OutOfSampleDuration.Ticks,
            Windows = schedule.Windows.Select(static window => new
            {
                window.Index,
                window.Split.StartInclusive,
                window.Split.TrainEndExclusive,
                window.Split.ValidationEndExclusive,
                window.Split.OutOfSampleEndExclusive
            }).ToArray()
        });
        var runHash = Hash(new
        {
            SchemaVersion,
            ScheduleSha256 = scheduleHash,
            Manifests = materialized.Select(static result => result.Manifest.ManifestSha256).ToArray()
        });
        var reportHash = Hash(new
        {
            SchemaVersion,
            RunSha256 = runHash,
            Windows = materialized.Select(static result => new
            {
                result.Index,
                result.Manifest.ManifestSha256,
                result.Execution.InitialQuoteBalance,
                result.Execution.EndingCashBalance,
                result.Execution.OpenQuantity,
                result.Execution.NetLiquidationValue,
                result.Execution.GrossReturnPercent,
                result.Execution.NetReturnPercent,
                result.Execution.RealizedPnl,
                result.Execution.GrossProfit,
                result.Execution.GrossLoss,
                result.Execution.Expectancy,
                result.Execution.TotalFees,
                result.Execution.EstimatedSpreadCost,
                result.Execution.EstimatedSlippageCost,
                result.Execution.MaximumDrawdownPercent,
                result.Execution.FillCount,
                result.Execution.CompletedTradeCount,
                result.Execution.WinningTradeCount,
                result.Execution.WinRatePercent,
                result.Execution.ProfitFactor,
                AverageHoldingTicks = result.Execution.AverageHoldingTime?.Ticks,
                result.Execution.HasPendingExecution,
                result.Execution.FirstFillAt,
                result.Execution.LastFillAt
            }).ToArray()
        });

        var returns = materialized
            .Select(static result => result.Execution.NetReturnPercent)
            .Order()
            .ToArray();
        var totalReturn = returns.Aggregate(0m, Add);
        var totalDrawdown = materialized.Aggregate(
            0m,
            static (sum, result) => Add(sum, result.Execution.MaximumDrawdownPercent));
        var totalFees = materialized.Aggregate(
            0m,
            static (sum, result) => Add(sum, result.Execution.TotalFees));
        var compoundedFactor = materialized.Aggregate(
            1m,
            static (factor, result) => Multiply(
                factor,
                Add(1m, result.Execution.NetReturnPercent / 100m)));
        var totalCompletedTrades = materialized.Aggregate(
            0,
            static (sum, result) => Add(sum, result.Execution.CompletedTradeCount));

        return new WalkForwardReport(
            SchemaVersion,
            scheduleHash,
            runHash,
            reportHash,
            firstManifest.StrategyId,
            firstManifest.StrategyVersion,
            schedule.TrainingMode,
            schedule.TrainingDuration,
            schedule.ValidationDuration,
            schedule.OutOfSampleDuration,
            Array.AsReadOnly(materialized),
            materialized.Count(static result => result.Execution.NetReturnPercent > 0m),
            totalCompletedTrades,
            totalFees,
            totalReturn / materialized.Length,
            Median(returns),
            returns[0],
            returns[^1],
            Multiply(Add(compoundedFactor, -1m), 100m),
            totalDrawdown / materialized.Length);
    }

    private static void ValidateWindow(
        WalkForwardWindow expected,
        WalkForwardWindowResult actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(actual.Manifest);
        ArgumentNullException.ThrowIfNull(actual.Execution);
        if (actual.Index != expected.Index || actual.Manifest.Split != expected.Split ||
            actual.Manifest.Purpose != BacktestRunPurpose.FinalOutOfSampleEvaluation ||
            actual.Manifest.Partitions.Count != 1 ||
            actual.Manifest.Partitions[0] != BacktestDatasetPartition.OutOfSample ||
            !IsSha256(actual.Manifest.ManifestSha256))
        {
            throw new DomainRuleViolationException(
                "Walk-forward result does not match its final OOS schedule window.");
        }

        ValidateExecution(actual.Execution);
    }

    private static void ValidateExecution(BacktestExecutionReport report)
    {
        decimal expectedReturn;
        decimal expectedGrossReturn;
        decimal? expectedWinRate;
        decimal? expectedProfitFactor;
        decimal? expectedExpectancy;
        try
        {
            expectedReturn = report.InitialQuoteBalance <= 0m
                ? decimal.MinValue
                : ((report.NetLiquidationValue - report.InitialQuoteBalance) /
                   report.InitialQuoteBalance) * 100m;
            expectedGrossReturn = report.InitialQuoteBalance <= 0m
                ? decimal.MinValue
                : ((report.NetLiquidationValue + report.TotalFees +
                    report.EstimatedSpreadCost + report.EstimatedSlippageCost -
                    report.InitialQuoteBalance) / report.InitialQuoteBalance) * 100m;
            expectedWinRate = report.CompletedTradeCount == 0
                ? null
                : (decimal?)report.WinningTradeCount / report.CompletedTradeCount * 100m;
            expectedProfitFactor = report.GrossLoss == 0m
                ? null
                : report.GrossProfit / report.GrossLoss;
            expectedExpectancy = report.CompletedTradeCount == 0
                ? null
                : (report.GrossProfit - report.GrossLoss) / report.CompletedTradeCount;
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Walk-forward execution return overflowed.");
        }

        if (report.InitialQuoteBalance <= 0m || report.EndingCashBalance < 0m ||
            report.OpenQuantity < 0m || report.NetLiquidationValue < 0m ||
            report.NetReturnPercent != expectedReturn ||
            report.GrossReturnPercent != expectedGrossReturn || report.GrossLoss < 0m ||
            report.GrossProfit < 0m || report.TotalFees < 0m ||
            report.EstimatedSpreadCost < 0m || report.EstimatedSlippageCost < 0m ||
            report.MaximumDrawdownPercent is < 0m or > 100m ||
            report.FillCount < 0 || report.CompletedTradeCount < 0 ||
            report.WinningTradeCount < 0 ||
            report.WinningTradeCount > report.CompletedTradeCount ||
            report.WinRatePercent is < 0m or > 100m || report.ProfitFactor < 0m ||
            report.WinRatePercent != expectedWinRate ||
            report.ProfitFactor != expectedProfitFactor ||
            report.Expectancy != expectedExpectancy ||
            report.AverageHoldingTime < TimeSpan.Zero ||
            IsNonUtc(report.FirstFillAt) || IsNonUtc(report.LastFillAt) ||
            report.FirstFillAt > report.LastFillAt)
        {
            throw new DomainRuleViolationException("Walk-forward execution report is invalid.");
        }

        if ((report.CompletedTradeCount == 0) != (report.WinRatePercent is null) ||
            (report.CompletedTradeCount == 0) != (report.Expectancy is null) ||
            (report.CompletedTradeCount == 0) != (report.AverageHoldingTime is null))
        {
            throw new DomainRuleViolationException(
                "Walk-forward trade statistics are internally inconsistent.");
        }
    }

    private static decimal Median(decimal[] ordered) =>
        ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : Add(ordered[(ordered.Length / 2) - 1], ordered[ordered.Length / 2]) / 2m;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character =>
            character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool IsNonUtc(DateTimeOffset? value) =>
        value.HasValue && value.Value.Offset != TimeSpan.Zero;

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static decimal Add(decimal left, decimal right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Walk-forward aggregate metric overflowed.");
        }
    }

    private static int Add(int left, int right)
    {
        try
        {
            return checked(left + right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Walk-forward trade count overflowed.");
        }
    }

    private static decimal Multiply(decimal left, decimal right)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Walk-forward aggregate metric overflowed.");
        }
    }
}
