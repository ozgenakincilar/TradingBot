using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Tests;

public sealed class MarketDataIntegrityGuardTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StreamEventCannotMakeInstrumentReadyBeforeRecoverySnapshot()
    {
        var guard = new MarketDataIntegrityGuard(Instrument);

        var result = guard.Observe(Cursor(101, Now));

        Assert.Equal(MarketDataIntegrityStatus.AwaitingRecovery, result.Status);
        Assert.False(result.IsReady);
        Assert.Null(result.LastAcceptedSequence);
    }

    [Fact]
    public void RecoverySnapshotAlignsSequenceAndNextEventIsAccepted()
    {
        var guard = ReadyGuard(100);

        var result = guard.Observe(Cursor(101, Now.AddMilliseconds(1)));

        Assert.Equal(MarketDataIntegrityStatus.Accepted, result.Status);
        Assert.True(result.IsReady);
        Assert.Equal(101, result.LastAcceptedSequence);
        Assert.Equal(102, result.ExpectedSequence);
    }

    [Fact]
    public void SequenceGapPausesInstrumentUntilRecoverySnapshot()
    {
        var guard = ReadyGuard(100);

        var gap = guard.Observe(Cursor(102, Now.AddMilliseconds(2)));
        var whilePaused = guard.Observe(Cursor(101, Now.AddMilliseconds(3)));
        var recovered = guard.ApplyRecoverySnapshot(Cursor(102, Now.AddMilliseconds(4), "recovery"));

        Assert.Equal(MarketDataIntegrityStatus.GapDetected, gap.Status);
        Assert.False(gap.IsReady);
        Assert.Equal(101, gap.ExpectedSequence);
        Assert.Equal(MarketDataIntegrityStatus.AwaitingRecovery, whilePaused.Status);
        Assert.Equal(MarketDataIntegrityStatus.RecoveryApplied, recovered.Status);
        Assert.True(recovered.IsReady);
        Assert.Equal(102, recovered.LastAcceptedSequence);
    }

    [Fact]
    public void DuplicateEventIsIgnoredWithoutPausingInstrument()
    {
        var guard = ReadyGuard(100);

        var duplicate = guard.Observe(Cursor(100, Now, "recovery"));

        Assert.Equal(MarketDataIntegrityStatus.Duplicate, duplicate.Status);
        Assert.True(duplicate.IsReady);
        Assert.Equal(100, duplicate.LastAcceptedSequence);
    }

    [Fact]
    public void SameSequenceWithDifferentEventIdPausesInstrument()
    {
        var guard = ReadyGuard(100);

        var conflict = guard.Observe(Cursor(100, Now, "conflicting-event"));

        Assert.Equal(MarketDataIntegrityStatus.ConflictingSequence, conflict.Status);
        Assert.False(conflict.IsReady);
        Assert.Equal(100, conflict.LastAcceptedSequence);
    }

    [Fact]
    public void LateOldEventIsIgnoredWithoutRewindingCursor()
    {
        var guard = ReadyGuard(100);

        var old = guard.Observe(Cursor(99, Now.AddMilliseconds(1)));

        Assert.Equal(MarketDataIntegrityStatus.OutOfOrder, old.Status);
        Assert.True(old.IsReady);
        Assert.Equal(100, old.LastAcceptedSequence);
    }

    [Fact]
    public void TimestampRegressionPausesInstrument()
    {
        var guard = ReadyGuard(100);

        var regression = guard.Observe(Cursor(101, Now.AddMilliseconds(-1)));

        Assert.Equal(MarketDataIntegrityStatus.TimestampRegression, regression.Status);
        Assert.False(regression.IsReady);
        Assert.Equal(100, regression.LastAcceptedSequence);
    }

    [Fact]
    public void StaleOrFutureReceiveTimeIsNotFresh()
    {
        var guard = ReadyGuard(100);

        Assert.True(guard.IsFresh(Now.AddSeconds(2), TimeSpan.FromSeconds(3)));
        Assert.False(guard.IsFresh(Now.AddSeconds(4), TimeSpan.FromSeconds(3)));
        Assert.False(guard.IsFresh(Now.AddSeconds(-1), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void OlderRecoverySnapshotCannotRewindAcceptedState()
    {
        var guard = ReadyGuard(100);

        var rejected = guard.ApplyRecoverySnapshot(Cursor(99, Now.AddMilliseconds(1), "old-recovery"));

        Assert.Equal(MarketDataIntegrityStatus.RecoveryRejected, rejected.Status);
        Assert.True(rejected.IsReady);
        Assert.Equal(100, rejected.LastAcceptedSequence);
    }

    private static MarketDataIntegrityGuard ReadyGuard(long sequence)
    {
        var guard = new MarketDataIntegrityGuard(Instrument);
        var recovered = guard.ApplyRecoverySnapshot(Cursor(sequence, Now, "recovery"));
        Assert.Equal(MarketDataIntegrityStatus.RecoveryApplied, recovered.Status);
        return guard;
    }

    private static MarketDataCursor Cursor(
        long sequence,
        DateTimeOffset timestamp,
        string? eventId = null) =>
        new(
            Instrument,
            eventId ?? $"event-{sequence}",
            sequence,
            timestamp,
            timestamp);
}
