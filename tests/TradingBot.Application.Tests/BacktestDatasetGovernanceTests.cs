using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class BacktestDatasetGovernanceTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ChronologicalDatasetSplit Split = ChronologicalDatasetSplit.Create(
        Start,
        Start.AddMonths(6),
        Start.AddMonths(9),
        Start.AddMonths(12));

    [Fact]
    public void SplitClassifiesBoundariesWithoutOverlap()
    {
        Assert.Equal(BacktestDatasetPartition.Excluded, Split.Classify(Start.AddTicks(-1)));
        Assert.Equal(BacktestDatasetPartition.Train, Split.Classify(Start));
        Assert.Equal(BacktestDatasetPartition.Validation, Split.Classify(Split.TrainEndExclusive));
        Assert.Equal(BacktestDatasetPartition.OutOfSample, Split.Classify(Split.ValidationEndExclusive));
        Assert.Equal(BacktestDatasetPartition.Excluded, Split.Classify(Split.OutOfSampleEndExclusive));
    }

    [Fact]
    public void ParameterSelectionCannotReadOutOfSamplePartition()
    {
        var action = () => BacktestExperimentPlan.Create(
            BacktestRunPurpose.ParameterSelection,
            BacktestDatasetPartition.Train,
            BacktestDatasetPartition.OutOfSample);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void FinalEvaluationMustUseOutOfSampleAlone()
    {
        var action = () => BacktestExperimentPlan.Create(
            BacktestRunPurpose.FinalOutOfSampleEvaluation,
            BacktestDatasetPartition.Validation,
            BacktestDatasetPartition.OutOfSample);

        Assert.Throws<DomainRuleViolationException>(action);
        var accepted = BacktestExperimentPlan.Create(
            BacktestRunPurpose.FinalOutOfSampleEvaluation,
            BacktestDatasetPartition.OutOfSample);
        Assert.Equal([BacktestDatasetPartition.OutOfSample], accepted.Partitions);
    }

    [Fact]
    public async Task ParameterSelectionStreamNeverYieldsOutOfSampleCandles()
    {
        var plan = BacktestExperimentPlan.Create(
            BacktestRunPurpose.ParameterSelection,
            BacktestDatasetPartition.Train,
            BacktestDatasetPartition.Validation);

        var observed = await ReadPartitionedAsync(plan);

        Assert.Equal(
            [Start, Split.TrainEndExclusive, Split.ValidationEndExclusive.AddHours(-1)],
            observed.Select(static candle => candle.OpenTime));
    }

    [Fact]
    public async Task FinalEvaluationStreamYieldsOnlyOutOfSampleCandles()
    {
        var plan = BacktestExperimentPlan.Create(
            BacktestRunPurpose.FinalOutOfSampleEvaluation,
            BacktestDatasetPartition.OutOfSample);

        var observed = await ReadPartitionedAsync(plan);

        var candle = Assert.Single(observed);
        Assert.Equal(Split.ValidationEndExclusive, candle.OpenTime);
    }

    [Fact]
    public void SameInputsProduceSameManifestAndSeedChangesOnlyManifestIdentity()
    {
        var first = CreateManifest(randomSeed: 42);
        var second = CreateManifest(randomSeed: 42);
        var changedSeed = CreateManifest(randomSeed: 43);

        Assert.Equal(first.ManifestSha256, second.ManifestSha256);
        Assert.Equal(first.DataSha256, second.DataSha256);
        Assert.Equal(first.ConfigurationSha256, second.ConfigurationSha256);
        Assert.Equal(first.Partitions, second.Partitions);
        Assert.Equal(64, first.ManifestSha256.Length);
        Assert.Equal(first.DataSha256, changedSeed.DataSha256);
        Assert.Equal(first.ConfigurationSha256, changedSeed.ConfigurationSha256);
        Assert.NotEqual(first.ManifestSha256, changedSeed.ManifestSha256);
    }

    [Fact]
    public void IncompleteDatasetCannotProduceManifest()
    {
        var action = () => BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor("signal-data", Signal, 'A'),
            signalSummary: null,
            Descriptor("trend-data", Trend, 'B'),
            Summary(),
            Split,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.ParameterSelection,
                BacktestDatasetPartition.Train,
                BacktestDatasetPartition.Validation),
            randomSeed: 42);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void SplitBoundaryMustAlignToBothDatasetTimeframes()
    {
        var misalignedSplit = ChronologicalDatasetSplit.Create(
            Start,
            Start.AddMonths(6).AddMinutes(15),
            Start.AddMonths(9),
            Start.AddMonths(12));

        var action = () => BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor("signal-data", Signal, 'A'),
            Summary(),
            Descriptor("trend-data", Trend, 'B'),
            Summary(),
            misalignedSplit,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.ParameterSelection,
                BacktestDatasetPartition.Train),
            randomSeed: 42);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void BothDatasetsMustCoverTheEntireSplit()
    {
        var incompleteTrendSummary = new HistoricalCandleDatasetSummary(
            CandleCount: 8_000,
            Start,
            Split.OutOfSampleEndExclusive.AddHours(-1));

        var action = () => BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor("signal-data", Signal, 'A'),
            Summary(),
            Descriptor("trend-data", Trend, 'B'),
            incompleteTrendSummary,
            Split,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.FinalOutOfSampleEvaluation,
                BacktestDatasetPartition.OutOfSample),
            randomSeed: 42);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static BacktestRunManifest CreateManifest(int randomSeed) =>
        BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor("signal-data", Signal, 'A'),
            Summary(),
            Descriptor("trend-data", Trend, 'B'),
            Summary(),
            Split,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.ParameterSelection,
                BacktestDatasetPartition.Train,
                BacktestDatasetPartition.Validation),
            randomSeed);

    private static async Task<List<Candle>> ReadPartitionedAsync(BacktestExperimentPlan plan)
    {
        DateTimeOffset[] openTimes =
        [
            Start.AddHours(-1),
            Start,
            Split.TrainEndExclusive,
            Split.ValidationEndExclusive.AddHours(-1),
            Split.ValidationEndExclusive,
            Split.OutOfSampleEndExclusive
        ];
        var results = new List<Candle>();
        await foreach (var candle in BacktestPartitionedCandleStream.ReadAsync(
                           ToAsync(openTimes.Select(CandleAt)),
                           Split,
                           plan,
                           CancellationToken.None))
        {
            results.Add(candle);
        }

        return results;
    }

    private static async IAsyncEnumerable<Candle> ToAsync(IEnumerable<Candle> candles)
    {
        foreach (var candle in candles)
        {
            yield return candle;
        }

        await Task.CompletedTask;
    }

    private static Candle CandleAt(DateTimeOffset openTime) =>
        Candle.CreateClosed(
            Instrument,
            Trend,
            openTime,
            Split.OutOfSampleEndExclusive.AddHours(2),
            100m,
            100m,
            100m,
            100m,
            1m);

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
        new(
            CandleCount: 35_040,
            Start,
            Start.AddMonths(12));

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
        InitialQuoteBalance: 10_000m,
        AssetCode.Create("BTC"),
        AssetCode.Create("USDT"),
        Percentage.FromPercent(10m),
        SyntheticSpreadBasisPoints: 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100),
            Percentage.FromPercent(0.1m),
            SlippageBasisPoints: 10m,
            Percentage.FromPercent(5m)));
}
