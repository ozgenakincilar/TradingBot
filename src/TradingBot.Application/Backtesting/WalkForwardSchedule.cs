using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public enum WalkForwardTrainingMode
{
    Rolling = 1,
    Expanding = 2
}

public sealed record WalkForwardWindow
{
    private WalkForwardWindow(int index, ChronologicalDatasetSplit split)
    {
        Index = index;
        Split = split;
    }

    public int Index { get; }

    public ChronologicalDatasetSplit Split { get; }

    internal static WalkForwardWindow Create(int index, ChronologicalDatasetSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        if (index < 0)
        {
            throw new DomainRuleViolationException("Walk-forward window index cannot be negative.");
        }

        return new WalkForwardWindow(index, split);
    }
}

public sealed record WalkForwardSchedule
{
    public const int MaximumWindowCount = 10_000;

    private WalkForwardSchedule(
        WalkForwardTrainingMode trainingMode,
        TimeSpan trainingDuration,
        TimeSpan validationDuration,
        TimeSpan outOfSampleDuration,
        IReadOnlyList<WalkForwardWindow> windows)
    {
        TrainingMode = trainingMode;
        TrainingDuration = trainingDuration;
        ValidationDuration = validationDuration;
        OutOfSampleDuration = outOfSampleDuration;
        Windows = windows;
    }

    public WalkForwardTrainingMode TrainingMode { get; }

    public TimeSpan TrainingDuration { get; }

    public TimeSpan ValidationDuration { get; }

    public TimeSpan OutOfSampleDuration { get; }

    public IReadOnlyList<WalkForwardWindow> Windows { get; }

    public static WalkForwardSchedule Create(
        DateTimeOffset datasetStartInclusive,
        DateTimeOffset datasetEndExclusive,
        TimeSpan trainingDuration,
        TimeSpan validationDuration,
        TimeSpan outOfSampleDuration,
        WalkForwardTrainingMode trainingMode,
        Timeframe signalTimeframe,
        Timeframe trendTimeframe)
    {
        ValidateInputs(
            datasetStartInclusive,
            datasetEndExclusive,
            trainingDuration,
            validationDuration,
            outOfSampleDuration,
            trainingMode,
            signalTimeframe,
            trendTimeframe);

        var windows = new List<WalkForwardWindow>();
        var rollingStart = datasetStartInclusive;
        var trainingEnd = Add(datasetStartInclusive, trainingDuration);
        while (true)
        {
            var validationEnd = Add(trainingEnd, validationDuration);
            var outOfSampleEnd = Add(validationEnd, outOfSampleDuration);
            if (outOfSampleEnd > datasetEndExclusive)
            {
                break;
            }

            if (windows.Count == MaximumWindowCount)
            {
                throw new DomainRuleViolationException(
                    "Walk-forward schedule exceeds the bounded window count.");
            }

            var split = ChronologicalDatasetSplit.Create(
                trainingMode == WalkForwardTrainingMode.Expanding
                    ? datasetStartInclusive
                    : rollingStart,
                trainingEnd,
                validationEnd,
                outOfSampleEnd);
            windows.Add(WalkForwardWindow.Create(windows.Count, split));

            rollingStart = Add(rollingStart, outOfSampleDuration);
            trainingEnd = Add(trainingEnd, outOfSampleDuration);
        }

        if (windows.Count == 0)
        {
            throw new DomainRuleViolationException(
                "Dataset does not contain one complete walk-forward window.");
        }

        return new WalkForwardSchedule(
            trainingMode,
            trainingDuration,
            validationDuration,
            outOfSampleDuration,
            Array.AsReadOnly(windows.ToArray()));
    }

    private static void ValidateInputs(
        DateTimeOffset datasetStartInclusive,
        DateTimeOffset datasetEndExclusive,
        TimeSpan trainingDuration,
        TimeSpan validationDuration,
        TimeSpan outOfSampleDuration,
        WalkForwardTrainingMode trainingMode,
        Timeframe signalTimeframe,
        Timeframe trendTimeframe)
    {
        if (datasetStartInclusive == default || datasetEndExclusive == default ||
            datasetStartInclusive.Offset != TimeSpan.Zero ||
            datasetEndExclusive.Offset != TimeSpan.Zero ||
            datasetStartInclusive >= datasetEndExclusive ||
            !Enum.IsDefined(trainingMode) ||
            signalTimeframe == default || trendTimeframe == default ||
            !signalTimeframe.IsBoundary(datasetStartInclusive) ||
            !trendTimeframe.IsBoundary(datasetStartInclusive) ||
            !signalTimeframe.IsBoundary(datasetEndExclusive) ||
            !trendTimeframe.IsBoundary(datasetEndExclusive))
        {
            throw new DomainRuleViolationException(
                "Walk-forward dataset range and training mode are invalid.");
        }

        TimeSpan[] durations = [trainingDuration, validationDuration, outOfSampleDuration];
        if (durations.Any(duration =>
                duration <= TimeSpan.Zero ||
                duration.Ticks % signalTimeframe.Duration.Ticks != 0 ||
                duration.Ticks % trendTimeframe.Duration.Ticks != 0))
        {
            throw new DomainRuleViolationException(
                "Walk-forward durations must be positive and align to both timeframes.");
        }
    }

    private static DateTimeOffset Add(DateTimeOffset value, TimeSpan duration)
    {
        try
        {
            return value.Add(duration);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "Walk-forward window exceeds the supported timestamp range.");
        }
    }
}
