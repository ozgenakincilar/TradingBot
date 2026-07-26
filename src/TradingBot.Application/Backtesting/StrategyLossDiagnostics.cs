using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record StrategyLossWindowDiagnostics(
    int Index,
    BacktestRunManifest Manifest,
    BacktestExecutionDiagnosticsReport Diagnostics);

public sealed record StrategyLossReasonSummary(
    string ExitReasonCode,
    int TradeCount,
    int WinningTradeCount,
    decimal NetPnl,
    decimal TotalEstimatedExecutionCost,
    decimal AverageMaximumFavorableExcursionPercent,
    decimal AverageMaximumAdverseExcursionPercent,
    int FavorableExcursionGivenBackTradeCount);

public sealed record StrategyLossDiagnosticsReport(
    string SchemaVersion,
    string RunSha256,
    string ReportSha256,
    string StrategyId,
    int StrategyVersion,
    IReadOnlyList<StrategyLossWindowDiagnostics> Windows,
    IReadOnlyList<StrategyLossReasonSummary> ExitReasonSummaries,
    int CompletedTradeCount,
    int LosingTradeCount,
    decimal TotalNetPnl,
    decimal TotalEstimatedExecutionCost,
    int FavorableExcursionGivenBackTradeCount);

public sealed class StrategyLossDiagnosticsOrchestrator
{
    public const string SchemaVersion = "strategy-loss-diagnostics-v1";

    private readonly IHistoricalCandleDatasetFactory _datasets;
    private readonly DeterministicStrategyBacktest _strategyBacktest;
    private readonly BacktestExecutionSimulator _executionSimulator;

    public StrategyLossDiagnosticsOrchestrator(
        IHistoricalCandleDatasetFactory datasets,
        DeterministicStrategyBacktest strategyBacktest,
        BacktestExecutionSimulator executionSimulator)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(strategyBacktest);
        ArgumentNullException.ThrowIfNull(executionSimulator);
        _datasets = datasets;
        _strategyBacktest = strategyBacktest;
        _executionSimulator = executionSimulator;
    }

    public async Task<StrategyLossDiagnosticsReport> RunAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        int randomSeed,
        BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executionPolicy);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(diagnosticsPolicy);
        executionPolicy.Validate(definition.SignalTimeframe, definition.InstrumentId);
        diagnosticsPolicy.Validate();
        var windows = new List<StrategyLossWindowDiagnostics>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            await using var signal = await _datasets.OpenAsync(
                definition.InstrumentId, definition.SignalTimeframe, cancellationToken);
            await using var trend = await _datasets.OpenAsync(
                definition.InstrumentId, definition.TrendTimeframe, cancellationToken);
            var split = window.Split;
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
            var counter = new DecisionCounter();
            var diagnostics = await _executionSimulator.RunWithDiagnosticsAsync(
                definition,
                CountAsync(decisions, counter, cancellationToken),
                executionPolicy,
                diagnosticsPolicy,
                cancellationToken);
            if (counter.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "Strategy loss diagnostics window produced no validation decisions.");
            }

            var manifest = BacktestRunManifestFactory.Create(
                definition,
                executionPolicy,
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
            windows.Add(new StrategyLossWindowDiagnostics(window.Index, manifest, diagnostics));
        }

        return CreateReport(definition, windows);
    }

    private static StrategyLossDiagnosticsReport CreateReport(
        StrategyDefinition definition,
        IReadOnlyList<StrategyLossWindowDiagnostics> windows)
    {
        var trades = windows.SelectMany(static window => window.Diagnostics.Trades).ToArray();
        var summaries = trades
            .GroupBy(static trade => trade.ExitReasonCode, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group =>
            {
                var values = group.ToArray();
                return new StrategyLossReasonSummary(
                    group.Key,
                    values.Length,
                    values.Count(static trade => trade.NetPnl > 0m),
                    values.Aggregate(0m, static (sum, trade) => Add(sum, trade.NetPnl)),
                    values.Aggregate(0m, static (sum, trade) => Add(sum, TotalCost(trade))),
                    Average(values, static trade => trade.MaximumFavorableExcursionPercent),
                    Average(values, static trade => trade.MaximumAdverseExcursionPercent),
                    values.Count(static trade =>
                        trade.MaximumFavorableExcursionPercent > 0m && trade.NetPnl <= 0m));
            })
            .ToArray();
        var runHash = Hash(new
        {
            SchemaVersion,
            Manifests = windows.Select(static window => window.Manifest.ManifestSha256).ToArray()
        });
        var reportHash = Hash(new
        {
            SchemaVersion,
            RunSha256 = runHash,
            Diagnostics = windows.Select(static window => window.Diagnostics.ReportSha256).ToArray()
        });
        return new StrategyLossDiagnosticsReport(
            SchemaVersion,
            runHash,
            reportHash,
            definition.StrategyId,
            definition.Version,
            windows.ToArray(),
            summaries,
            trades.Length,
            trades.Count(static trade => trade.NetPnl <= 0m),
            trades.Aggregate(0m, static (sum, trade) => Add(sum, trade.NetPnl)),
            trades.Aggregate(0m, static (sum, trade) => Add(sum, TotalCost(trade))),
            trades.Count(static trade =>
                trade.MaximumFavorableExcursionPercent > 0m && trade.NetPnl <= 0m));
    }

    private static decimal TotalCost(BacktestTradeAttribution trade) =>
        Add(Add(trade.EstimatedFees, trade.EstimatedSpreadCost), trade.EstimatedSlippageCost);

    private static decimal Average(
        IReadOnlyCollection<BacktestTradeAttribution> trades,
        Func<BacktestTradeAttribution, decimal> selector) =>
        trades.Aggregate(0m, (sum, trade) => Add(sum, selector(trade))) / trades.Count;

    private static decimal Add(decimal left, decimal right)
    {
        try { return checked(left + right); }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Strategy loss diagnostics aggregate metric overflowed.");
        }
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

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

    private sealed class DecisionCounter
    {
        public int Count { get; private set; }

        public void Increment()
        {
            try { Count = checked(Count + 1); }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Strategy loss diagnostics decision count overflowed.");
            }
        }
    }
}
