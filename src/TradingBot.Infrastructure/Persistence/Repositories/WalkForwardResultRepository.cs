using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Backtesting;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class WalkForwardResultRepository(TradingBotDbContext context)
    : IWalkForwardResultRepository
{
    public async Task<StoredWalkForwardResult?> GetAsync(
        string runSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runSha256);
        return await context.WalkForwardRuns
            .AsNoTracking()
            .Where(run => run.RunSha256 == runSha256)
            .Select(static run => new StoredWalkForwardResult(run.RunSha256, run.ReportSha256))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(WalkForwardReport report, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        var entity = new WalkForwardRunEntity
        {
            RunSha256 = report.RunSha256,
            ScheduleSha256 = report.ScheduleSha256,
            ReportSha256 = report.ReportSha256,
            SchemaVersion = report.SchemaVersion,
            StrategyId = report.StrategyId,
            StrategyVersion = report.StrategyVersion,
            TrainingMode = (int)report.TrainingMode,
            TrainingDurationTicks = report.TrainingDuration.Ticks,
            ValidationDurationTicks = report.ValidationDuration.Ticks,
            OutOfSampleDurationTicks = report.OutOfSampleDuration.Ticks,
            WindowCount = report.Windows.Count,
            ProfitableWindowCount = report.ProfitableWindowCount,
            TotalCompletedTradeCount = report.TotalCompletedTradeCount,
            TotalFees = report.TotalFees,
            MeanNetReturnPercent = report.MeanNetReturnPercent,
            MedianNetReturnPercent = report.MedianNetReturnPercent,
            WorstNetReturnPercent = report.WorstNetReturnPercent,
            BestNetReturnPercent = report.BestNetReturnPercent,
            CompoundedNetReturnPercent = report.CompoundedNetReturnPercent,
            MeanMaximumDrawdownPercent = report.MeanMaximumDrawdownPercent,
            CreatedAt = createdAt
        };
        foreach (var result in report.Windows)
        {
            var execution = result.Execution;
            var split = result.Manifest.Split;
            entity.Windows.Add(new WalkForwardWindowResultEntity
            {
                RunSha256 = report.RunSha256,
                WindowIndex = result.Index,
                ManifestSha256 = result.Manifest.ManifestSha256,
                TrainStartInclusive = split.StartInclusive,
                TrainEndExclusive = split.TrainEndExclusive,
                ValidationEndExclusive = split.ValidationEndExclusive,
                OutOfSampleEndExclusive = split.OutOfSampleEndExclusive,
                InitialQuoteBalance = execution.InitialQuoteBalance,
                EndingCashBalance = execution.EndingCashBalance,
                OpenQuantity = execution.OpenQuantity,
                NetLiquidationValue = execution.NetLiquidationValue,
                GrossReturnPercent = execution.GrossReturnPercent,
                NetReturnPercent = execution.NetReturnPercent,
                RealizedPnl = execution.RealizedPnl,
                GrossProfit = execution.GrossProfit,
                GrossLoss = execution.GrossLoss,
                Expectancy = execution.Expectancy,
                TotalFees = execution.TotalFees,
                EstimatedSpreadCost = execution.EstimatedSpreadCost,
                EstimatedSlippageCost = execution.EstimatedSlippageCost,
                MaximumDrawdownPercent = execution.MaximumDrawdownPercent,
                FillCount = execution.FillCount,
                CompletedTradeCount = execution.CompletedTradeCount,
                WinningTradeCount = execution.WinningTradeCount,
                WinRatePercent = execution.WinRatePercent,
                ProfitFactor = execution.ProfitFactor,
                AverageHoldingTimeTicks = execution.AverageHoldingTime?.Ticks,
                HasPendingExecution = execution.HasPendingExecution,
                FirstFillAt = execution.FirstFillAt,
                LastFillAt = execution.LastFillAt
            });
        }

        context.WalkForwardRuns.Add(entity);
    }
}
