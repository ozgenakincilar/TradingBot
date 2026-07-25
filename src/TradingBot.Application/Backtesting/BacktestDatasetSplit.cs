using TradingBot.Domain.Common;

namespace TradingBot.Application.Backtesting;

public enum BacktestDatasetPartition
{
    Excluded = 0,
    Train = 1,
    Validation = 2,
    OutOfSample = 3
}

public enum BacktestRunPurpose
{
    ParameterSelection = 1,
    FinalOutOfSampleEvaluation = 2
}

public sealed record ChronologicalDatasetSplit
{
    private ChronologicalDatasetSplit(
        DateTimeOffset startInclusive,
        DateTimeOffset trainEndExclusive,
        DateTimeOffset validationEndExclusive,
        DateTimeOffset outOfSampleEndExclusive)
    {
        StartInclusive = startInclusive;
        TrainEndExclusive = trainEndExclusive;
        ValidationEndExclusive = validationEndExclusive;
        OutOfSampleEndExclusive = outOfSampleEndExclusive;
    }

    public DateTimeOffset StartInclusive { get; }

    public DateTimeOffset TrainEndExclusive { get; }

    public DateTimeOffset ValidationEndExclusive { get; }

    public DateTimeOffset OutOfSampleEndExclusive { get; }

    public static ChronologicalDatasetSplit Create(
        DateTimeOffset startInclusive,
        DateTimeOffset trainEndExclusive,
        DateTimeOffset validationEndExclusive,
        DateTimeOffset outOfSampleEndExclusive)
    {
        if (!IsUtc(startInclusive) || !IsUtc(trainEndExclusive) ||
            !IsUtc(validationEndExclusive) || !IsUtc(outOfSampleEndExclusive) ||
            startInclusive >= trainEndExclusive ||
            trainEndExclusive >= validationEndExclusive ||
            validationEndExclusive >= outOfSampleEndExclusive)
        {
            throw new DomainRuleViolationException(
                "Backtest dataset split must contain ordered, non-overlapping UTC ranges.");
        }

        return new ChronologicalDatasetSplit(
            startInclusive,
            trainEndExclusive,
            validationEndExclusive,
            outOfSampleEndExclusive);
    }

    public BacktestDatasetPartition Classify(DateTimeOffset openTime)
    {
        if (!IsUtc(openTime))
        {
            throw new DomainRuleViolationException("Backtest candle time must be UTC.");
        }

        if (openTime < StartInclusive || openTime >= OutOfSampleEndExclusive)
        {
            return BacktestDatasetPartition.Excluded;
        }

        if (openTime < TrainEndExclusive)
        {
            return BacktestDatasetPartition.Train;
        }

        return openTime < ValidationEndExclusive
            ? BacktestDatasetPartition.Validation
            : BacktestDatasetPartition.OutOfSample;
    }

    private static bool IsUtc(DateTimeOffset value) =>
        value != default && value.Offset == TimeSpan.Zero;
}

public sealed record BacktestExperimentPlan
{
    private BacktestExperimentPlan(
        BacktestRunPurpose purpose,
        IReadOnlyList<BacktestDatasetPartition> partitions)
    {
        Purpose = purpose;
        Partitions = partitions;
    }

    public BacktestRunPurpose Purpose { get; }

    public IReadOnlyList<BacktestDatasetPartition> Partitions { get; }

    public static BacktestExperimentPlan Create(
        BacktestRunPurpose purpose,
        params BacktestDatasetPartition[] partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        if (!Enum.IsDefined(purpose) || partitions.Length == 0 ||
            partitions.Any(static partition =>
                partition is BacktestDatasetPartition.Excluded || !Enum.IsDefined(partition)) ||
            partitions.Distinct().Count() != partitions.Length)
        {
            throw new DomainRuleViolationException("Backtest experiment partitions are invalid.");
        }

        var allowed = purpose switch
        {
            BacktestRunPurpose.ParameterSelection =>
                partitions.All(static partition =>
                    partition is BacktestDatasetPartition.Train or BacktestDatasetPartition.Validation) &&
                partitions.Contains(BacktestDatasetPartition.Train),
            BacktestRunPurpose.FinalOutOfSampleEvaluation =>
                partitions is [BacktestDatasetPartition.OutOfSample],
            _ => false
        };
        if (!allowed)
        {
            throw new DomainRuleViolationException(
                "Out-of-sample data is locked from parameter selection and must run alone.");
        }

        return new BacktestExperimentPlan(purpose, Array.AsReadOnly(partitions.ToArray()));
    }
}
