using TradingBot.Application.Backtesting;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class WalkForwardReportTests
{
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));

    [Fact]
    public void AggregatesOrderedOosWindowsAndProducesStableHashes()
    {
        var schedule = Schedule();
        var inputs = Results(schedule, secondReturn: -5m);

        var first = WalkForwardReportFactory.Create(schedule, inputs);
        var second = WalkForwardReportFactory.Create(schedule, inputs);

        Assert.Equal(first.ScheduleSha256, second.ScheduleSha256);
        Assert.Equal(first.RunSha256, second.RunSha256);
        Assert.Equal(first.ReportSha256, second.ReportSha256);
        Assert.Equal(1, first.ProfitableWindowCount);
        Assert.Equal(3m, first.TotalFees);
        Assert.Equal(2.5m, first.MeanNetReturnPercent);
        Assert.Equal(2.5m, first.MedianNetReturnPercent);
        Assert.Equal(-5m, first.WorstNetReturnPercent);
        Assert.Equal(10m, first.BestNetReturnPercent);
        Assert.Equal(4.5m, first.CompoundedNetReturnPercent);
        Assert.Equal(4m, first.MeanMaximumDrawdownPercent);
    }

    [Fact]
    public void ChangedOutcomeChangesOnlyReportIdentity()
    {
        var schedule = Schedule();
        var baseline = WalkForwardReportFactory.Create(schedule, Results(schedule, -5m));
        var changed = WalkForwardReportFactory.Create(schedule, Results(schedule, -4m));

        Assert.Equal(baseline.ScheduleSha256, changed.ScheduleSha256);
        Assert.Equal(baseline.RunSha256, changed.RunSha256);
        Assert.NotEqual(baseline.ReportSha256, changed.ReportSha256);
    }

    [Fact]
    public void ParameterSelectionResultCannotEnterOosReport()
    {
        var schedule = Schedule();
        var results = Results(schedule, -5m);
        results[0] = results[0] with
        {
            Manifest = Manifest(
                schedule.Windows[0].Split,
                BacktestExperimentPlan.Create(
                    BacktestRunPurpose.ParameterSelection,
                    BacktestDatasetPartition.Train,
                    BacktestDatasetPartition.Validation))
        };

        var action = () => WalkForwardReportFactory.Create(schedule, results);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void MissingOrMisorderedWindowIsRejected()
    {
        var schedule = Schedule();
        var missing = Results(schedule, -5m).Take(1);
        var reversed = Results(schedule, -5m).Reverse();

        Assert.Throws<DomainRuleViolationException>(
            () => WalkForwardReportFactory.Create(schedule, missing));
        Assert.Throws<DomainRuleViolationException>(
            () => WalkForwardReportFactory.Create(schedule, reversed));
    }

    [Fact]
    public async Task PersistenceIsIdempotentForTheSameReport()
    {
        var report = WalkForwardReportFactory.Create(Schedule(), Results(Schedule(), -5m));
        var repository = new FakeRepository();
        var handler = new PersistWalkForwardReport(repository, new InlineUnitOfWork());

        var stored = await handler.HandleAsync(report, Start, CancellationToken.None);
        var duplicate = await handler.HandleAsync(report, Start, CancellationToken.None);

        Assert.Equal(WalkForwardPersistenceStatus.Stored, stored);
        Assert.Equal(WalkForwardPersistenceStatus.AlreadyStored, duplicate);
        Assert.Equal(1, repository.AddCount);
    }

    [Fact]
    public async Task ConflictingOutcomeForSameRunIsRejected()
    {
        var schedule = Schedule();
        var report = WalkForwardReportFactory.Create(schedule, Results(schedule, -5m));
        var repository = new FakeRepository
        {
            Stored = new StoredWalkForwardResult(report.RunSha256, new string('F', 64))
        };
        var handler = new PersistWalkForwardReport(repository, new InlineUnitOfWork());

        var action = () => handler.HandleAsync(report, Start, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, repository.AddCount);
    }

    private static WalkForwardSchedule Schedule() => WalkForwardSchedule.Create(
        Start,
        Start.AddDays(270),
        TimeSpan.FromDays(180),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(30),
        WalkForwardTrainingMode.Rolling,
        Signal,
        Trend);

    private static WalkForwardWindowResult[] Results(
        WalkForwardSchedule schedule,
        decimal secondReturn) =>
    [
        Result(schedule.Windows[0], 10m, maximumDrawdown: 2m, fees: 1m),
        Result(schedule.Windows[1], secondReturn, maximumDrawdown: 6m, fees: 2m)
    ];

    private static WalkForwardWindowResult Result(
        WalkForwardWindow window,
        decimal netReturn,
        decimal maximumDrawdown,
        decimal fees)
    {
        const decimal initial = 1_000m;
        var netLiquidation = initial * (1m + (netReturn / 100m));
        return new WalkForwardWindowResult(
            window.Index,
            Manifest(
                window.Split,
                BacktestExperimentPlan.Create(
                    BacktestRunPurpose.FinalOutOfSampleEvaluation,
                    BacktestDatasetPartition.OutOfSample)),
            new BacktestExecutionReport(
                initial,
                netLiquidation,
                OpenQuantity: 0m,
                netLiquidation,
                GrossReturnPercent: netReturn + (fees / initial * 100m),
                NetReturnPercent: netReturn,
                RealizedPnl: netLiquidation - initial,
                GrossProfit: 0m,
                GrossLoss: 0m,
                Expectancy: null,
                TotalFees: fees,
                EstimatedSpreadCost: 0m,
                EstimatedSlippageCost: 0m,
                maximumDrawdown,
                FillCount: 0,
                CompletedTradeCount: 0,
                WinningTradeCount: 0,
                WinRatePercent: null,
                ProfitFactor: null,
                AverageHoldingTime: null,
                HasPendingExecution: false,
                FirstFillAt: null,
                LastFillAt: null));
    }

    private static BacktestRunManifest Manifest(
        ChronologicalDatasetSplit split,
        BacktestExperimentPlan plan) =>
        BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor("signal-data", Signal, 'A'),
            Summary(),
            Descriptor("trend-data", Trend, 'B'),
            Summary(),
            split,
            plan,
            randomSeed: 42);

    private static HistoricalCandleDatasetDescriptor Descriptor(
        string sourceId,
        Timeframe timeframe,
        char hashCharacter) =>
        new(
            sourceId,
            HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string(hashCharacter, 64),
            Instrument,
            timeframe);

    private static HistoricalCandleDatasetSummary Summary() =>
        new(CandleCount: 25_920, Start, Start.AddDays(270));

    private static StrategyDefinition Definition() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline",
        1,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 20,
        trendEmaPeriod: 200,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 200,
        minimumTrendWarmupCandles: 200);

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

    private sealed class FakeRepository : IWalkForwardResultRepository
    {
        public StoredWalkForwardResult? Stored { get; set; }

        public int AddCount { get; private set; }

        public Task<StoredWalkForwardResult?> GetAsync(
            string runSha256,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Stored is { } existing && existing.RunSha256 == runSha256 ? existing : null);
        }

        public void Add(WalkForwardReport report, DateTimeOffset createdAt)
        {
            AddCount++;
            Stored = new StoredWalkForwardResult(report.RunSha256, report.ReportSha256);
        }
    }

    private sealed class InlineUnitOfWork : ITradingUnitOfWork
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }
}
