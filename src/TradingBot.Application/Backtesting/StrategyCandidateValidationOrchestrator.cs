using System.Runtime.CompilerServices;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed class StrategyCandidateValidationOrchestrator
{
    private readonly IHistoricalCandleDatasetFactory _datasets;
    private readonly DeterministicStrategyBacktest _strategyBacktest;
    private readonly BacktestExecutionSimulator _executionSimulator;
    private readonly BuyAndHoldBenchmark _benchmark;

    public StrategyCandidateValidationOrchestrator(
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

    public async Task<StrategyValidationReport> RunAsync(
        StrategyDefinition baseline,
        StrategyDefinition candidate,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        int randomSeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ValidateDefinitions(baseline, candidate);
        executionPolicy.Validate(baseline.SignalTimeframe);
        var results = new List<StrategyValidationWindowResult>(schedule.Windows.Count);
        foreach (var window in schedule.Windows)
        {
            var baselineRun = await RunDefinitionAsync(
                baseline, executionPolicy, window, randomSeed, cancellationToken);
            var candidateRun = await RunDefinitionAsync(
                candidate, executionPolicy, window, randomSeed, cancellationToken);
            await using var benchmarkDataset = await _datasets.OpenAsync(
                baseline.InstrumentId, baseline.SignalTimeframe, cancellationToken);
            var benchmarkReport = await _benchmark.RunRangeAsync(
                benchmarkDataset.ReadAsync(cancellationToken),
                window.Split.TrainEndExclusive,
                window.Split.ValidationEndExclusive,
                executionPolicy,
                baseline.InstrumentId,
                baseline.SignalTimeframe,
                cancellationToken);
            results.Add(new StrategyValidationWindowResult(
                window.Index,
                baselineRun.Manifest,
                baselineRun.Execution,
                candidateRun.Manifest,
                candidateRun.Execution,
                benchmarkReport));
        }

        return StrategyCandidateValidationReportFactory.Create(schedule, results);
    }

    private async Task<DefinitionRun> RunDefinitionAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy policy,
        WalkForwardWindow window,
        int randomSeed,
        CancellationToken cancellationToken)
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
        var execution = await _executionSimulator.RunAsync(
            definition, CountAsync(decisions, counter, cancellationToken), policy,
            cancellationToken);
        if (counter.Count == 0)
        {
            throw new DomainRuleViolationException(
                "Strategy validation window produced no validation decisions.");
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
        return new DefinitionRun(manifest, execution);
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
        if (baseline.StrategyId != candidate.StrategyId ||
            baseline.InstrumentId != candidate.InstrumentId ||
            baseline.SignalTimeframe != candidate.SignalTimeframe ||
            baseline.TrendTimeframe != candidate.TrendTimeframe ||
            baseline.Version >= candidate.Version ||
            baseline.SignalEmaHysteresisBasisPoints != 0m ||
            candidate.SignalEmaHysteresisBasisPoints <= 0m)
        {
            throw new DomainRuleViolationException(
                "Strategy validation requires compatible baseline and candidate versions.");
        }
    }

    private sealed record DefinitionRun(
        BacktestRunManifest Manifest,
        BacktestExecutionReport Execution);

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
                    "Strategy validation decision count overflowed.");
            }
        }
    }
}
