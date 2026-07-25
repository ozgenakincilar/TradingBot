using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class WalkForwardScheduleTests
{
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));

    [Fact]
    public void RollingScheduleMovesFixedTrainingWindowByEachOutOfSamplePeriod()
    {
        var schedule = Create(WalkForwardTrainingMode.Rolling);

        Assert.Equal(3, schedule.Windows.Count);
        Assert.Equal(Start, schedule.Windows[0].Split.StartInclusive);
        Assert.Equal(Start.AddDays(30), schedule.Windows[1].Split.StartInclusive);
        Assert.Equal(Start.AddDays(60), schedule.Windows[2].Split.StartInclusive);
        Assert.All(
            schedule.Windows,
            window => Assert.Equal(
                TimeSpan.FromDays(180),
                window.Split.TrainEndExclusive - window.Split.StartInclusive));
    }

    [Fact]
    public void ExpandingScheduleKeepsStartAndAddsObservedHistory()
    {
        var schedule = Create(WalkForwardTrainingMode.Expanding);

        Assert.Equal(3, schedule.Windows.Count);
        Assert.All(schedule.Windows, window => Assert.Equal(Start, window.Split.StartInclusive));
        Assert.Equal(Start.AddDays(180), schedule.Windows[0].Split.TrainEndExclusive);
        Assert.Equal(Start.AddDays(210), schedule.Windows[1].Split.TrainEndExclusive);
        Assert.Equal(Start.AddDays(240), schedule.Windows[2].Split.TrainEndExclusive);
    }

    [Fact]
    public void OutOfSampleWindowsAreContiguousAndNeverOverlap()
    {
        var schedule = Create(WalkForwardTrainingMode.Rolling);

        for (var index = 1; index < schedule.Windows.Count; index++)
        {
            Assert.Equal(
                schedule.Windows[index - 1].Split.OutOfSampleEndExclusive,
                schedule.Windows[index].Split.ValidationEndExclusive);
        }

        Assert.Equal(
            schedule.Windows.Count,
            schedule.Windows
                .Select(static window => window.Split.ValidationEndExclusive)
                .Distinct()
                .Count());
    }

    [Fact]
    public void SameInputsProduceSameOrderedWindows()
    {
        var first = Create(WalkForwardTrainingMode.Rolling);
        var second = Create(WalkForwardTrainingMode.Rolling);

        Assert.Equal(first.Windows, second.Windows);
        Assert.Equal(Enumerable.Range(0, first.Windows.Count), first.Windows.Select(static x => x.Index));
    }

    [Fact]
    public void MisalignedDurationIsRejected()
    {
        var action = () => WalkForwardSchedule.Create(
            Start,
            Start.AddDays(300),
            TimeSpan.FromDays(180).Add(TimeSpan.FromMinutes(15)),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30),
            WalkForwardTrainingMode.Rolling,
            Signal,
            Trend);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void IncompleteDatasetWindowIsRejected()
    {
        var action = () => WalkForwardSchedule.Create(
            Start,
            Start.AddDays(239),
            TimeSpan.FromDays(180),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30),
            WalkForwardTrainingMode.Rolling,
            Signal,
            Trend);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void ExcessiveWindowCountIsRejected()
    {
        var oneSecond = Timeframe.Create(TimeSpan.FromSeconds(1));
        var action = () => WalkForwardSchedule.Create(
            Start,
            Start.AddSeconds(WalkForwardSchedule.MaximumWindowCount + 3L),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            WalkForwardTrainingMode.Rolling,
            oneSecond,
            oneSecond);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static WalkForwardSchedule Create(WalkForwardTrainingMode mode) =>
        WalkForwardSchedule.Create(
            Start,
            Start.AddDays(300),
            TimeSpan.FromDays(180),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30),
            mode,
            Signal,
            Trend);
}
