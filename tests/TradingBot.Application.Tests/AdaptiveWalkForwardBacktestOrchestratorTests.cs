using System.Runtime.CompilerServices;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class AdaptiveWalkForwardBacktestOrchestratorTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromMinutes(5));

    [Fact]
    public void ParameterGridSnapshotsInputAndRejectsDuplicates()
    {
        var source = new[] { AtrHysteresisParameterCandidate.Create(2, 0.1m) };
        var grid = AtrHysteresisParameterGrid.Create(source);

        source[0] = AtrHysteresisParameterCandidate.Create(3, 0.2m);

        Assert.Equal(AtrHysteresisParameterCandidate.Create(2, 0.1m), grid[0]);
        Assert.Throws<DomainRuleViolationException>(() =>
            AtrHysteresisParameterGrid.Create(
                AtrHysteresisParameterCandidate.Create(2, 0.1m),
                AtrHysteresisParameterCandidate.Create(2, 0.1m)));
    }

    [Fact]
    public async Task AdaptiveRunRejectsLegacyDefinitionBeforeOpeningDatasets()
    {
        var datasets = new InMemoryDatasetFactory(SignalCandles(98m), TrendCandles());
        var action = () => CreateOrchestrator(datasets).RunAdaptiveAsync(
            LegacyDefinition(),
            ExecutionPolicy(),
            Schedule(),
            AtrHysteresisParameterGrid.Create(
                AtrHysteresisParameterCandidate.Create(2, 0.1m)),
            42,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, datasets.OpenCount);
    }

    [Fact]
    public async Task SelectsOnValidationAndAppliesCandidateToUntouchedOosWindow()
    {
        var grid = AtrHysteresisParameterGrid.Create(
            AtrHysteresisParameterCandidate.Create(2, 10m),
            AtrHysteresisParameterCandidate.Create(2, 0.1m));
        var datasets = new InMemoryDatasetFactory(SignalCandles(98m), TrendCandles());

        var report = await CreateOrchestrator(datasets).RunAdaptiveAsync(
            Definition(),
            ExecutionPolicy(),
            Schedule(),
            grid,
            42,
            CancellationToken.None);

        var selection = Assert.Single(report.Selections);
        Assert.Equal(AtrHysteresisParameterCandidate.Create(2, 0.1m), selection.Candidate);
        Assert.Equal(Start, selection.HistoryStartInclusive);
        Assert.Equal(Start.AddMinutes(25), selection.ValidationStartInclusive);
        Assert.Equal(Start.AddMinutes(40), selection.SelectionEndExclusive);
        Assert.True(selection.ValidationCompletedTradeCount > 0);
        Assert.Equal(BacktestRunPurpose.FinalOutOfSampleEvaluation,
            Assert.Single(report.OutOfSampleReport.Windows).Manifest.Purpose);
        Assert.Equal(7, datasets.OpenCount);
    }

    [Fact]
    public async Task OosPriceChangesCannotInfluenceParameterSelection()
    {
        var grid = AtrHysteresisParameterGrid.Create(
            AtrHysteresisParameterCandidate.Create(2, 10m),
            AtrHysteresisParameterCandidate.Create(2, 0.1m));
        var first = await CreateOrchestrator(
                new InMemoryDatasetFactory(SignalCandles(98m), TrendCandles()))
            .RunAdaptiveAsync(
                Definition(), ExecutionPolicy(), Schedule(), grid, 42, CancellationToken.None);
        var second = await CreateOrchestrator(
                new InMemoryDatasetFactory(SignalCandles(150m), TrendCandles(50m)))
            .RunAdaptiveAsync(
                Definition(), ExecutionPolicy(), Schedule(), grid, 42, CancellationToken.None);

        var firstSelection = Assert.Single(first.Selections);
        var secondSelection = Assert.Single(second.Selections);
        Assert.Equal(firstSelection.Candidate, secondSelection.Candidate);
        Assert.Equal(firstSelection.ProfitFactorScore, secondSelection.ProfitFactorScore);
        Assert.Equal(firstSelection.ValidationNetReturnPercent,
            secondSelection.ValidationNetReturnPercent);
        Assert.Equal(firstSelection.ValidationMaximumDrawdownPercent,
            secondSelection.ValidationMaximumDrawdownPercent);
        Assert.Equal(firstSelection.ValidationCompletedTradeCount,
            secondSelection.ValidationCompletedTradeCount);
        Assert.Equal(firstSelection.SignalHistorySha256,
            secondSelection.SignalHistorySha256);
        Assert.Equal(firstSelection.TrendHistorySha256,
            secondSelection.TrendHistorySha256);
    }

    [Fact]
    public async Task EqualPerformanceUsesDeterministicParameterTieBreak()
    {
        var report = await CreateOrchestrator(
                new InMemoryDatasetFactory(SignalCandles(98m), TrendCandles()))
            .RunAdaptiveAsync(
                Definition(),
                ExecutionPolicy(),
                Schedule(),
                AtrHysteresisParameterGrid.Create(
                    AtrHysteresisParameterCandidate.Create(2, 0.2m),
                    AtrHysteresisParameterCandidate.Create(2, 0.1m)),
                42,
                CancellationToken.None);

        Assert.Equal(
            AtrHysteresisParameterCandidate.Create(2, 0.1m),
            Assert.Single(report.Selections).Candidate);
    }

    [Fact]
    public async Task NoCompletedValidationTradeFailsBeforeOosIsOpened()
    {
        var datasets = new InMemoryDatasetFactory(
            FlatSignalCandles(),
            TrendCandles());
        var action = () => CreateOrchestrator(datasets).RunAdaptiveAsync(
            Definition(),
            ExecutionPolicy(),
            Schedule(),
            AtrHysteresisParameterGrid.Create(
                AtrHysteresisParameterCandidate.Create(2, 0.1m)),
            42,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(2, datasets.OpenCount);
    }

    [Fact]
    public async Task CandidateBeyondRegisteredWarmupFailsBeforeOpeningDatasets()
    {
        var datasets = new InMemoryDatasetFactory(SignalCandles(98m), TrendCandles());
        var action = () => CreateOrchestrator(datasets).RunAdaptiveAsync(
            Definition(),
            ExecutionPolicy(),
            Schedule(),
            AtrHysteresisParameterGrid.Create(
                AtrHysteresisParameterCandidate.Create(3, 0.1m)),
            42,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, datasets.OpenCount);
    }

    private static WalkForwardBacktestOrchestrator CreateOrchestrator(
        IHistoricalCandleDatasetFactory datasets) => new(
            datasets,
            new DeterministicStrategyBacktest(),
            new BacktestExecutionSimulator(),
            new BuyAndHoldBenchmark());

    private static WalkForwardSchedule Schedule() => WalkForwardSchedule.Create(
        Start,
        Start.AddMinutes(50),
        TimeSpan.FromMinutes(25),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(10),
        WalkForwardTrainingMode.Rolling,
        Signal,
        Trend);

    private static StrategyDefinition Definition() => StrategyDefinition.Create(
        "adaptive-walk-forward-test",
        6,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 2,
        trendEmaPeriod: 2,
        maximumSignalCandleMovePercent: 10m,
        minimumSignalWarmupCandles: 4,
        minimumTrendWarmupCandles: 4,
        trendStrengthPeriod: 2,
        minimumTrendStrength: 1m,
        requirePositiveDirectionalMovement: true,
        signalAtrPeriod: 2,
        signalAtrHysteresisMultiplier: 0.2m);

    private static StrategyDefinition LegacyDefinition() => StrategyDefinition.Create(
        "legacy-walk-forward-test",
        1,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 2,
        trendEmaPeriod: 2,
        maximumSignalCandleMovePercent: 10m,
        minimumSignalWarmupCandles: 4,
        minimumTrendWarmupCandles: 4);

    private static BacktestExecutionPolicy ExecutionPolicy() => new(
        InitialQuoteBalance: 1_000m,
        AssetCode.Create("BTC"),
        AssetCode.Create("USDT"),
        Percentage.FromPercent(10m),
        SyntheticSpreadBasisPoints: 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100),
            Percentage.FromPercent(0.1m),
            SlippageBasisPoints: 10m,
            Percentage.FromPercent(5m)));

    private static IReadOnlyList<Candle> SignalCandles(decimal oosClose)
    {
        var candles = new Candle[50];
        for (var index = 0; index < candles.Length; index++)
        {
            var close = index switch
            {
                26 or 27 => 102m,
                >= 28 and < 40 => 98m,
                >= 40 => oosClose,
                _ => 100m
            };
            var open = index switch
            {
                26 => 100m,
                28 => 102m,
                >= 40 => oosClose,
                _ => close
            };
            candles[index] = CandleAt(
                Signal,
                Start + (Signal.Duration * index),
                open,
                Math.Max(open, close) + 0.25m,
                Math.Min(open, close) - 0.25m,
                close);
        }

        return candles;
    }

    private static IReadOnlyList<Candle> FlatSignalCandles()
    {
        var candles = new Candle[50];
        for (var index = 0; index < candles.Length; index++)
        {
            candles[index] = CandleAt(
                Signal,
                Start + (Signal.Duration * index),
                100m,
                100.25m,
                99.75m,
                100m);
        }

        return candles;
    }

    private static IReadOnlyList<Candle> TrendCandles(decimal oosOffset = 0m)
    {
        var candles = new Candle[10];
        for (var index = 0; index < candles.Length; index++)
        {
            var close = 90m + index + (index >= 8 ? oosOffset : 0m);
            candles[index] = CandleAt(
                Trend,
                Start + (Trend.Duration * index),
                close - 0.5m,
                close + 0.5m,
                close - 1m,
                close);
        }

        return candles;
    }

    private static Candle CandleAt(
        Timeframe timeframe,
        DateTimeOffset openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close) => Candle.CreateClosed(
            Instrument,
            timeframe,
            openTime,
            Start.AddHours(2),
            open,
            high,
            low,
            close,
            1_000m);

    private sealed class InMemoryDatasetFactory(
        IReadOnlyList<Candle> signals,
        IReadOnlyList<Candle> trends) : IHistoricalCandleDatasetFactory
    {
        public int OpenCount { get; private set; }

        public ValueTask<IHistoricalCandleDataset> OpenAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            var candles = timeframe == Signal
                ? signals
                : timeframe == Trend
                    ? trends
                    : throw new DomainRuleViolationException("Unexpected test timeframe.");
            return ValueTask.FromResult<IHistoricalCandleDataset>(new InMemoryDataset(
                candles,
                new HistoricalCandleDatasetDescriptor(
                    timeframe == Signal ? "adaptive-signal" : "adaptive-trend",
                    HistoricalCandleDatasetContract.CsvSchemaVersion,
                    timeframe == Signal ? new string('C', 64) : new string('D', 64),
                    instrumentId,
                    timeframe)));
        }
    }

    private sealed class InMemoryDataset(
        IReadOnlyList<Candle> candles,
        HistoricalCandleDatasetDescriptor descriptor) : IHistoricalCandleDataset
    {
        private int _readStarted;

        public HistoricalCandleDatasetDescriptor Descriptor { get; } = descriptor;

        public HistoricalCandleDatasetSummary? CompletedSummary { get; private set; }

        public IAsyncEnumerable<Candle> ReadAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _readStarted, 1) != 0)
            {
                throw new InvalidOperationException("Test dataset is single use.");
            }

            return ReadCoreAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async IAsyncEnumerable<Candle> ReadCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < candles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candles[index];
            }

            CompletedSummary = new HistoricalCandleDatasetSummary(
                candles.Count,
                candles[0].OpenTime,
                candles[^1].CloseTime);
            await Task.CompletedTask;
        }
    }
}
