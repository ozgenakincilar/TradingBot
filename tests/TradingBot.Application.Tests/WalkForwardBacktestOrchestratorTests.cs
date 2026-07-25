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

public sealed class WalkForwardBacktestOrchestratorTests
{
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromMinutes(5));

    [Fact]
    public async Task RunsEveryWindowSequentiallyWithFreshStreamingDatasets()
    {
        var schedule = Schedule();
        var datasets = new InMemoryDatasetFactory(SignalCandles(), TrendCandles());
        var orchestrator = CreateOrchestrator(datasets);

        var report = await orchestrator.RunAsync(
            Definition(),
            ExecutionPolicy(),
            schedule,
            randomSeed: 42,
            CancellationToken.None);

        Assert.Equal(2, report.Windows.Count);
        Assert.Equal(4, datasets.OpenCount);
        Assert.All(report.Windows, static result =>
        {
            Assert.Equal(BacktestRunPurpose.FinalOutOfSampleEvaluation, result.Manifest.Purpose);
            Assert.Equal([BacktestDatasetPartition.OutOfSample], result.Manifest.Partitions);
            Assert.Equal(0, result.Execution.FillCount);
            Assert.Equal(0m, result.Execution.NetReturnPercent);
            Assert.NotNull(result.Manifest.SignalSummary);
            Assert.NotNull(result.Manifest.TrendSummary);
        });
        Assert.Equal(schedule.Windows.Select(static window => window.Split),
            report.Windows.Select(static result => result.Manifest.Split));
    }

    [Fact]
    public async Task SameDatasetsAndInputsProduceSameWalkForwardReport()
    {
        var schedule = Schedule();
        var first = await CreateOrchestrator(
                new InMemoryDatasetFactory(SignalCandles(), TrendCandles()))
            .RunAsync(Definition(), ExecutionPolicy(), schedule, 42, CancellationToken.None);
        var second = await CreateOrchestrator(
                new InMemoryDatasetFactory(SignalCandles(), TrendCandles()))
            .RunAsync(Definition(), ExecutionPolicy(), schedule, 42, CancellationToken.None);

        Assert.Equal(first.ScheduleSha256, second.ScheduleSha256);
        Assert.Equal(first.RunSha256, second.RunSha256);
        Assert.Equal(first.ReportSha256, second.ReportSha256);
    }

    [Fact]
    public async Task InsufficientPreOosHistoryFailsBeforeOpeningDatasets()
    {
        var schedule = WalkForwardSchedule.Create(
            Start,
            Start.AddMinutes(20),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10),
            WalkForwardTrainingMode.Rolling,
            Signal,
            Trend);
        var datasets = new InMemoryDatasetFactory(SignalCandles(), TrendCandles());

        var action = () => CreateOrchestrator(datasets).RunAsync(
            Definition(minimumTrendWarmupCandles: 3),
            ExecutionPolicy(),
            schedule,
            42,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, datasets.OpenCount);
    }

    private static WalkForwardBacktestOrchestrator CreateOrchestrator(
        IHistoricalCandleDatasetFactory datasets) =>
        new(datasets, new DeterministicStrategyBacktest(), new BacktestExecutionSimulator());

    private static WalkForwardSchedule Schedule() => WalkForwardSchedule.Create(
        Start,
        Start.AddMinutes(35),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        WalkForwardTrainingMode.Rolling,
        Signal,
        Trend);

    private static IReadOnlyList<Candle> SignalCandles() => Enumerable.Range(0, 35)
        .Select(index => CandleAt(Signal, Start + (Signal.Duration * index)))
        .ToArray();

    private static IReadOnlyList<Candle> TrendCandles() => Enumerable.Range(0, 7)
        .Select(index => CandleAt(Trend, Start + (Trend.Duration * index)))
        .ToArray();

    private static Candle CandleAt(Timeframe timeframe, DateTimeOffset openTime) =>
        Candle.CreateClosed(
            Instrument,
            timeframe,
            openTime,
            Start.AddHours(1),
            100m,
            100m,
            100m,
            100m,
            10m);

    private static StrategyDefinition Definition(int minimumTrendWarmupCandles = 2) =>
        StrategyDefinition.Create(
        "walk-forward-test",
        1,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 2,
        trendEmaPeriod: 2,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 3,
        minimumTrendWarmupCandles);

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
            var source = timeframe == Signal ? "signal-fixture" : "trend-fixture";
            var hash = timeframe == Signal ? new string('A', 64) : new string('B', 64);
            return ValueTask.FromResult<IHistoricalCandleDataset>(
                new InMemoryDataset(candles, new HistoricalCandleDatasetDescriptor(
                    source,
                    HistoricalCandleDatasetContract.CsvSchemaVersion,
                    hash,
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
            foreach (var candle in candles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candle;
            }

            CompletedSummary = new HistoricalCandleDatasetSummary(
                candles.Count,
                candles[0].OpenTime,
                candles[^1].CloseTime);
            await Task.CompletedTask;
        }
    }
}
