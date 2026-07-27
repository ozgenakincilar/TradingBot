using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public readonly record struct AtrHysteresisParameterCandidate(
    int AtrPeriod,
    decimal HysteresisMultiplier)
{
    public static AtrHysteresisParameterCandidate Create(
        int atrPeriod,
        decimal hysteresisMultiplier)
    {
        if (atrPeriod is < 2 or > 100 ||
            hysteresisMultiplier is <= 0m or > 10m)
        {
            throw new DomainRuleViolationException(
                "Adaptive ATR parameters must satisfy the v6 strategy bounds.");
        }

        return new AtrHysteresisParameterCandidate(atrPeriod, hysteresisMultiplier);
    }
}

public sealed class AtrHysteresisParameterGrid
{
    public const int MaximumCandidateCount = 64;
    private readonly AtrHysteresisParameterCandidate[] _candidates;

    private AtrHysteresisParameterGrid(AtrHysteresisParameterCandidate[] candidates)
    {
        _candidates = candidates;
    }

    public int Count => _candidates.Length;

    public AtrHysteresisParameterCandidate this[int index] => _candidates[index];

    public static AtrHysteresisParameterGrid Create(
        params AtrHysteresisParameterCandidate[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Length is 0 or > MaximumCandidateCount)
        {
            throw new DomainRuleViolationException(
                "Adaptive ATR grid must contain between one and 64 candidates.");
        }

        var snapshot = new AtrHysteresisParameterCandidate[candidates.Length];
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = AtrHysteresisParameterCandidate.Create(
                candidates[index].AtrPeriod,
                candidates[index].HysteresisMultiplier);
            for (var prior = 0; prior < index; prior++)
            {
                if (snapshot[prior] == candidate)
                {
                    throw new DomainRuleViolationException(
                        "Adaptive ATR grid cannot contain duplicate candidates.");
                }
            }

            snapshot[index] = candidate;
        }

        return new AtrHysteresisParameterGrid(snapshot);
    }
}

public sealed record AtrHysteresisParameterSelection(
    int WindowIndex,
    AtrHysteresisParameterCandidate Candidate,
    DateTimeOffset HistoryStartInclusive,
    DateTimeOffset ValidationStartInclusive,
    DateTimeOffset SelectionEndExclusive,
    decimal ProfitFactorScore,
    decimal ValidationNetReturnPercent,
    decimal ValidationMaximumDrawdownPercent,
    int ValidationCompletedTradeCount,
    string SignalHistorySha256,
    string TrendHistorySha256);

public sealed record AdaptiveWalkForwardReport(
    WalkForwardReport OutOfSampleReport,
    IReadOnlyList<AtrHysteresisParameterSelection> Selections);

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
            results.Add(await RunOutOfSampleWindowAsync(
                definition,
                executionPolicy,
                window,
                randomSeed,
                cancellationToken));
        }

        return WalkForwardReportFactory.Create(schedule, results);
    }

    public async Task<AdaptiveWalkForwardReport> RunAdaptiveAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardSchedule schedule,
        AtrHysteresisParameterGrid parameterGrid,
        int randomSeed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(executionPolicy);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(parameterGrid);
        if (definition.Version != 6)
        {
            throw new DomainRuleViolationException(
                "Adaptive ATR walk-forward selection is available only for strategy v6.");
        }

        ValidateWarmupCoverage(definition, schedule);
        ValidateParameterGrid(definition, parameterGrid);
        executionPolicy.Validate(definition.SignalTimeframe);

        var results = new List<WalkForwardWindowResult>(schedule.Windows.Count);
        var selections = new AtrHysteresisParameterSelection[schedule.Windows.Count];
        foreach (var window in schedule.Windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = await SelectParametersAsync(
                definition,
                executionPolicy,
                window,
                parameterGrid,
                cancellationToken);
            selections[window.Index] = selection;
            var selectedDefinition = WithAtrParameters(definition, selection.Candidate);
            results.Add(await RunOutOfSampleWindowAsync(
                selectedDefinition,
                executionPolicy,
                window,
                randomSeed,
                cancellationToken));
        }

        return new AdaptiveWalkForwardReport(
            WalkForwardReportFactory.Create(schedule, results),
            Array.AsReadOnly(selections));
    }

    private async Task<AtrHysteresisParameterSelection> SelectParametersAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardWindow window,
        AtrHysteresisParameterGrid parameterGrid,
        CancellationToken cancellationToken)
    {
        CandidateScore best = default;
        var hasBest = false;
        for (var index = 0; index < parameterGrid.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = parameterGrid[index];
            var candidateDefinition = WithAtrParameters(definition, candidate);
            var evaluated = await EvaluateSelectionCandidateAsync(
                candidateDefinition,
                executionPolicy,
                window.Split,
                cancellationToken);
            if (!evaluated.IsEligible)
            {
                continue;
            }

            var score = new CandidateScore(candidate, evaluated);
            if (!hasBest || score.IsBetterThan(best))
            {
                best = score;
                hasBest = true;
            }
        }

        if (!hasBest)
        {
            throw new DomainRuleViolationException(
                "Adaptive ATR selection requires at least one candidate with a completed validation trade.");
        }

        return new AtrHysteresisParameterSelection(
            window.Index,
            best.Candidate,
            window.Split.StartInclusive,
            window.Split.TrainEndExclusive,
            window.Split.ValidationEndExclusive,
            best.Evaluation.ProfitFactorScore,
            best.Evaluation.Report.NetReturnPercent,
            best.Evaluation.Report.MaximumDrawdownPercent,
            best.Evaluation.Report.CompletedTradeCount,
            best.Evaluation.SignalHistorySha256,
            best.Evaluation.TrendHistorySha256);
    }

    private async Task<CandidateEvaluation> EvaluateSelectionCandidateAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        ChronologicalDatasetSplit split,
        CancellationToken cancellationToken)
    {
        await using var signalDataset = await _datasets.OpenAsync(
            definition.InstrumentId,
            definition.SignalTimeframe,
            cancellationToken);
        await using var trendDataset = await _datasets.OpenAsync(
            definition.InstrumentId,
            definition.TrendTimeframe,
            cancellationToken);
        using var signalHistory = new SelectionHistoryHasher();
        using var trendHistory = new SelectionHistoryHasher();
        var decisions = _strategyBacktest.RunAsync(
            definition,
            ReadSelectionHistoryAsync(
                signalDataset.ReadAsync(cancellationToken),
                split.StartInclusive,
                split.ValidationEndExclusive,
                signalHistory,
                cancellationToken),
            ReadSelectionHistoryAsync(
                trendDataset.ReadAsync(cancellationToken),
                split.StartInclusive,
                split.ValidationEndExclusive,
                trendHistory,
                cancellationToken),
            split.TrainEndExclusive,
            cancellationToken);
        var counter = new DecisionCounter();
        var report = await _executionSimulator.RunAsync(
            definition,
            CountAsync(decisions, counter, cancellationToken),
            executionPolicy,
            cancellationToken);
        var eligible = counter.Count > 0 && report.CompletedTradeCount > 0;
        var profitFactorScore = !eligible
            ? decimal.MinValue
            : report.ProfitFactor ?? (report.GrossProfit > 0m ? decimal.MaxValue : 0m);
        return new CandidateEvaluation(
            report,
            eligible,
            profitFactorScore,
            signalHistory.Complete(),
            trendHistory.Complete());
    }

    private async Task<WalkForwardWindowResult> RunOutOfSampleWindowAsync(
        StrategyDefinition definition,
        BacktestExecutionPolicy executionPolicy,
        WalkForwardWindow window,
        int randomSeed,
        CancellationToken cancellationToken)
    {
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
        return new WalkForwardWindowResult(window.Index, manifest, execution, benchmark);
    }

    private static async IAsyncEnumerable<Candle> ReadSelectionHistoryAsync(
        IAsyncEnumerable<Candle> source,
        DateTimeOffset startInclusive,
        DateTimeOffset selectionEndExclusive,
        SelectionHistoryHasher history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var candle in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candle.OpenTime >= selectionEndExclusive)
            {
                yield break;
            }

            if (candle.OpenTime >= startInclusive)
            {
                history.Append(candle);
                yield return candle;
            }
        }
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

    private static void ValidateParameterGrid(
        StrategyDefinition definition,
        AtrHysteresisParameterGrid parameterGrid)
    {
        for (var index = 0; index < parameterGrid.Count; index++)
        {
            if (definition.MinimumSignalWarmupCandles <
                parameterGrid[index].AtrPeriod + 2)
            {
                throw new DomainRuleViolationException(
                    "Adaptive ATR candidate exceeds the registered signal warm-up capacity.");
            }
        }
    }

    private static StrategyDefinition WithAtrParameters(
        StrategyDefinition definition,
        AtrHysteresisParameterCandidate candidate) => StrategyDefinition.Create(
            definition.StrategyId,
            definition.Version,
            definition.InstrumentId,
            definition.SignalTimeframe,
            definition.TrendTimeframe,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            definition.SignalEmaHysteresisBasisPoints,
            definition.ReentryCooldownCandles,
            definition.ProfitProtectionActivationBasisPoints,
            definition.ProfitProtectionTrailingBasisPoints,
            definition.TrendStrengthPeriod,
            definition.MinimumTrendStrength,
            definition.RequirePositiveDirectionalMovement,
            candidate.AtrPeriod,
            candidate.HysteresisMultiplier);

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

    private readonly record struct CandidateEvaluation(
        BacktestExecutionReport Report,
        bool IsEligible,
        decimal ProfitFactorScore,
        string SignalHistorySha256,
        string TrendHistorySha256);

    private readonly record struct CandidateScore(
        AtrHysteresisParameterCandidate Candidate,
        CandidateEvaluation Evaluation)
    {
        public bool IsBetterThan(CandidateScore other)
        {
            var comparison = Evaluation.ProfitFactorScore.CompareTo(
                other.Evaluation.ProfitFactorScore);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            comparison = Evaluation.Report.NetReturnPercent.CompareTo(
                other.Evaluation.Report.NetReturnPercent);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            comparison = other.Evaluation.Report.MaximumDrawdownPercent.CompareTo(
                Evaluation.Report.MaximumDrawdownPercent);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            comparison = other.Candidate.AtrPeriod.CompareTo(Candidate.AtrPeriod);
            return comparison != 0
                ? comparison > 0
                : Candidate.HysteresisMultiplier < other.Candidate.HysteresisMultiplier;
        }
    }

    private sealed class SelectionHistoryHasher : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _completed;

        public void Append(Candle candle)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Selection history hash is already complete.");
            }

            Span<byte> buffer = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(
                buffer,
                candle.OpenTime.UtcDateTime.Ticks);
            _hash.AppendData(buffer[..sizeof(long)]);
            AppendDecimal(candle.Open, buffer);
            AppendDecimal(candle.High, buffer);
            AppendDecimal(candle.Low, buffer);
            AppendDecimal(candle.Close, buffer);
            AppendDecimal(candle.BaseVolume, buffer);
        }

        public string Complete()
        {
            if (_completed)
            {
                throw new InvalidOperationException("Selection history hash is already complete.");
            }

            _completed = true;
            return Convert.ToHexString(_hash.GetHashAndReset());
        }

        public void Dispose() => _hash.Dispose();

        private void AppendDecimal(decimal value, Span<byte> buffer)
        {
            Span<int> bits = stackalloc int[4];
            decimal.GetBits(value, bits);
            for (var index = 0; index < bits.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    buffer[(index * sizeof(int))..],
                    bits[index]);
            }

            _hash.AppendData(buffer);
        }
    }
}
