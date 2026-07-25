using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Tests;

public sealed class ClosedCandleSequenceGuardTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe OneMinute = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start = new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CandleIsWithheldUntilRecoveryIsApplied()
    {
        var guard = new ClosedCandleSequenceGuard(Instrument, OneMinute);

        var result = guard.Observe(CandleAt(0));

        Assert.Equal(ClosedCandleIntegrityStatus.AwaitingRecovery, result.Status);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void ContiguousRecoveryAndNextCandleAreAccepted()
    {
        var guard = new ClosedCandleSequenceGuard(Instrument, OneMinute);

        var recovery = guard.ApplyRecovery([CandleAt(0), CandleAt(1)]);
        var next = guard.Observe(CandleAt(2));

        Assert.Equal(ClosedCandleIntegrityStatus.RecoveryApplied, recovery.Status);
        Assert.Equal(ClosedCandleIntegrityStatus.Accepted, next.Status);
        Assert.True(next.IsReady);
        Assert.Equal(Start.AddMinutes(3), next.ExpectedOpenTime);
    }

    [Fact]
    public void GapPausesSeriesWithoutAdvancingLastAcceptedCandle()
    {
        var guard = ReadyGuard();

        var gap = guard.Observe(CandleAt(2));
        var withheld = guard.Observe(CandleAt(1));

        Assert.Equal(ClosedCandleIntegrityStatus.GapDetected, gap.Status);
        Assert.False(gap.IsReady);
        Assert.Equal(Start, gap.LastAcceptedOpenTime);
        Assert.Equal(ClosedCandleIntegrityStatus.AwaitingRecovery, withheld.Status);
    }

    [Fact]
    public void ConflictingCandleAtSameBoundaryPausesSeries()
    {
        var guard = ReadyGuard();
        var conflicting = Candle.CreateClosed(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(1),
            100m,
            111m,
            90m,
            105m,
            12m);

        var result = guard.Observe(conflicting);

        Assert.Equal(ClosedCandleIntegrityStatus.ConflictingCandle, result.Status);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void InvalidRecoveryIsAtomicAndFailClosed()
    {
        var guard = ReadyGuard();

        var result = guard.ApplyRecovery([CandleAt(1), CandleAt(3)]);

        Assert.Equal(ClosedCandleIntegrityStatus.RecoveryRejected, result.Status);
        Assert.False(result.IsReady);
        Assert.Equal(Start, result.LastAcceptedOpenTime);
    }

    [Fact]
    public void ExactRepeatedCandleIsDuplicateWithoutPausing()
    {
        var guard = ReadyGuard();

        var result = guard.Observe(CandleAt(0));

        Assert.Equal(ClosedCandleIntegrityStatus.Duplicate, result.Status);
        Assert.True(result.IsReady);
    }

    [Fact]
    public void RecoveryForAnotherInstrumentThrowsAndClosesReadiness()
    {
        var guard = ReadyGuard();
        var other = Candle.CreateClosed(
            InstrumentId.Create("OKX", "ETH-USDT"),
            OneMinute,
            Start.AddMinutes(1),
            Start.AddMinutes(2),
            100m,
            110m,
            90m,
            105m,
            12m);

        var action = () => guard.ApplyRecovery([other]);

        Assert.Throws<DomainRuleViolationException>(action);
        Assert.False(guard.IsReady);
    }

    private static ClosedCandleSequenceGuard ReadyGuard()
    {
        var guard = new ClosedCandleSequenceGuard(Instrument, OneMinute);
        guard.ApplyRecovery([CandleAt(0)]);
        return guard;
    }

    private static Candle CandleAt(int minute) =>
        Candle.CreateClosed(
            Instrument,
            OneMinute,
            Start.AddMinutes(minute),
            Start.AddMinutes(minute + 1),
            100m,
            110m,
            90m,
            105m,
            12m);
}
