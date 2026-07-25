using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.Tests;

public sealed class MarketDataReplayAlignerTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
    private readonly MarketDataReplayAligner _aligner = new();

    [Fact]
    public void SnapshotOverlapIsDiscardedAndContiguousEventsArePublished()
    {
        var result = _aligner.Align(
            Event(100, "snapshot-100"),
            [Event(99), Event(100, "stream-100"), Event(101), Event(102)]);

        Assert.Equal(MarketDataReplayStatus.Aligned, result.Status);
        Assert.Equal([101L, 102L], result.Events.Select(x => x.Sequence));
        Assert.Equal(102, result.LastAcceptedSequence);
    }

    [Fact]
    public void ReplayGapPublishesNoPartialEvents()
    {
        var result = _aligner.Align(
            Event(100, "snapshot-100"),
            [Event(101), Event(103)]);

        Assert.Equal(MarketDataReplayStatus.GapDetected, result.Status);
        Assert.Empty(result.Events);
        Assert.Equal(101, result.LastAcceptedSequence);
    }

    [Fact]
    public void ConflictingDuplicatePublishesNoPartialEvents()
    {
        var result = _aligner.Align(
            Event(100, "snapshot-100"),
            [Event(101, "event-101-a"), Event(101, "event-101-b")]);

        Assert.Equal(MarketDataReplayStatus.ConflictingSequence, result.Status);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void TimestampRegressionPublishesNoPartialEvents()
    {
        var result = _aligner.Align(
            Event(100, "snapshot-100"),
            [Event(101, timestamp: Now.AddMilliseconds(-1))]);

        Assert.Equal(MarketDataReplayStatus.TimestampRegression, result.Status);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void ExactDuplicateInsideReplayIsIgnored()
    {
        var result = _aligner.Align(
            Event(100, "snapshot-100"),
            [Event(101), Event(101), Event(102)]);

        Assert.Equal(MarketDataReplayStatus.Aligned, result.Status);
        Assert.Equal([101L, 102L], result.Events.Select(x => x.Sequence));
    }

    [Fact]
    public void ExchangePreviousSequenceAlignsJumpingSequenceIds()
    {
        var first = Event(150) with { PreviousSequence = 100 };
        var second = Event(225) with { PreviousSequence = 150 };

        var result = _aligner.Align(Event(100, "snapshot-100"), [first, second]);

        Assert.Equal(MarketDataReplayStatus.Aligned, result.Status);
        Assert.Equal([150L, 225L], result.Events.Select(x => x.Sequence));
        Assert.Equal(225, result.LastAcceptedSequence);
    }

    private static PaperMarketEvent Event(
        long sequence,
        string? eventId = null,
        DateTimeOffset? timestamp = null)
    {
        var occurredAt = timestamp ?? Now.AddMilliseconds(sequence - 100);
        return new PaperMarketEvent(
            eventId ?? $"event-{sequence}",
            sequence,
            occurredAt,
            new PaperTopOfBookSnapshot(
                Instrument,
                Price.From(99m),
                1m,
                Price.From(100m),
                1m,
                occurredAt));
    }
}
