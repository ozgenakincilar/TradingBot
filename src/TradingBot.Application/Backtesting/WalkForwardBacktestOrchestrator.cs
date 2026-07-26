using System.Runtime.CompilerServices;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed class WalkForwardBacktestOrchestrator
{
    private readonly IHistoricalCandleDatasetFactory _datasets;
    private readonly DeterministicStrategyBacktest _strategyBacktest;
    private readonly BacktestExecutionSimulator _executionSimulator;
    private readonly BuyAndHoldBenchmark _benchmark;

    public WalkForwardBacktestOrchestrator(
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

    public async Task<WalkForwardReport> RunAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        int randomSeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executionPolicy);
        ArgumentNullException.ThrowIfNull(schedule);
        ValidateWarmupCoverage(definition, schedule);
        executionPolicy.Validate(definition.SignalTimeframe);

        var results = new List<WalkForwardWindowResult>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var signalDataset = await _datasets.OpenAsync(
                definition.InstrumentId,
                definition.SignalTimeframe,
                cancellationToken);
            await using var trendDataset = await _datasets.OpenAsync(
                definition.InstrumentId,
                definition.TrendTimeframe,
                cancellationToken);

            var split = window.Split;
            var decisions = _strategyBacktest.RunAsync(
                definition,
                BacktestWindowCandleStream.ReadAsync(
                    signalDataset.ReadAsync(cancellationToken),
                    split,
                    cancellationToken),
                BacktestWindowCandleStream.ReadAsync(
                    trendDataset.ReadAsync(cancellationToken),
                    split,
                    cancellationToken),
                split.ValidationEndExclusive,
                cancellationToken);
            var counter = new DecisionCounter();
            var execution = await _executionSimulator.RunAsync(
                definition,
                CountAsync(decisions, counter, cancellationToken),
                executionPolicy,
                cancellationToken);
            await using var benchmarkDataset = await _datasets.OpenAsync(
                definition.InstrumentId,
                definition.SignalTimeframe,
                cancellationToken);
            var benchmark = await _benchmark.RunAsync(
                benchmarkDataset.ReadAsync(cancellationToken),
                split,
                executionPolicy,
                definition.InstrumentId,
                definition.SignalTimeframe,
                cancellationToken);
            if (counter.Count == 0)
            {
                throw new DomainRuleViolationException(
                    "Walk-forward OOS window produced no strategy evaluations.");
            }

            var manifest = BacktestRunManifestFactory.Create(
                definition,
                executionPolicy,
                signalDataset.Descriptor,
                signalDataset.CompletedSummary,
                trendDataset.Descriptor,
                trendDataset.CompletedSummary,
                split,
                BacktestExperimentPlan.Create(
                    BacktestRunPurpose.FinalOutOfSampleEvaluation,
                    BacktestDatasetPartition.OutOfSample),
                randomSeed);
            results.Add(new WalkForwardWindowResult(window.Index, manifest, execution, benchmark));
        }

        return WalkForwardReportFactory.Create(schedule, results);
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

    private static void ValidateWarmupCoverage(
        StrategyDefinition definition,
        WalkForwardSchedule schedule)
    {
        foreach (var window in schedule.Windows)
        {
            var history = window.Split.ValidationEndExclusive - window.Split.StartInclusive;
            var requiredSignal = CalculateRequiredHistory(
                definition.SignalTimeframe,
                definition.MinimumSignalWarmupCandles);
            var requiredTrend = CalculateRequiredHistory(
                definition.TrendTimeframe,
                definition.MinimumTrendWarmupCandles);
            if (history < requiredSignal || history < requiredTrend)
            {
                throw new DomainRuleViolationException(
                    "Walk-forward train and validation history cannot satisfy strategy warm-up.");
            }
        }
    }

    private static TimeSpan CalculateRequiredHistory(Timeframe timeframe, int candleCount)
    {
        try
        {
            return TimeSpan.FromTicks(checked(timeframe.Duration.Ticks * candleCount));
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Strategy warm-up duration exceeds the supported time range.");
        }
    }

    private sealed class DecisionCounter
    {
        public int Count { get; private set; }

        public void Increment()
        {
            try
            {
                Count = checked(Count + 1);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Walk-forward strategy evaluation count overflowed.");
            }
        }
    }
}
