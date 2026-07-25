using TradingBot.Application.Abstractions;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class MarketSnapshotServiceTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FirstStreamEventUsesRecoverySnapshotBeforePublication()
    {
        var client = new RecordingClient(
            [Event(1, "stream-1")],
            [Event(1, "snapshot-1")]);
        var service = CreateService(client);

        var result = await service.GetAsync(
            Instrument,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(MarketDataIntegrityStatus.RecoveryApplied, result.IntegrityStatus);
        Assert.True(result.IsFresh);
        Assert.Equal("snapshot-1", result.MarketEvent?.EventId);
        Assert.Equal(1, client.RecoveryCalls);
    }

    [Fact]
    public async Task SequentialEventIsPublishedWithoutAnotherRecovery()
    {
        var client = new RecordingClient(
            [Event(1, "stream-1"), Event(2, "stream-2")],
            [Event(1, "snapshot-1")]);
        var service = CreateService(client);
        await service.GetAsync(Instrument, TimeSpan.FromSeconds(5), CancellationToken.None);

        var result = await service.GetAsync(
            Instrument,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(MarketDataIntegrityStatus.Accepted, result.IntegrityStatus);
        Assert.Equal("stream-2", result.MarketEvent?.EventId);
        Assert.Equal(1, client.RecoveryCalls);
    }

    [Fact]
    public async Task DuplicateEventIsWithheldFromExecutionPipeline()
    {
        var client = new RecordingClient(
            [Event(1, "stream-1"), Event(1, "snapshot-1")],
            [Event(1, "snapshot-1")]);
        var service = CreateService(client);
        await service.GetAsync(Instrument, TimeSpan.FromSeconds(5), CancellationToken.None);

        var duplicate = await service.GetAsync(
            Instrument,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(MarketDataIntegrityStatus.Duplicate, duplicate.IntegrityStatus);
        Assert.Null(duplicate.MarketEvent);
        Assert.Equal(1, client.RecoveryCalls);
    }

    [Fact]
    public async Task SequenceGapFetchesRecoverySnapshotAndPublishesRecoveredState()
    {
        var client = new RecordingClient(
            [Event(1, "stream-1"), Event(3, "stream-3")],
            [Event(1, "snapshot-1"), Event(3, "snapshot-3")]);
        var service = CreateService(client);
        await service.GetAsync(Instrument, TimeSpan.FromSeconds(5), CancellationToken.None);

        var recovered = await service.GetAsync(
            Instrument,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(MarketDataIntegrityStatus.RecoveryApplied, recovered.IntegrityStatus);
        Assert.Equal("snapshot-3", recovered.MarketEvent?.EventId);
        Assert.Equal(2, client.RecoveryCalls);
    }

    [Fact]
    public async Task StaleRecoverySnapshotIsNotPublished()
    {
        var stale = Now.AddSeconds(-10);
        var client = new RecordingClient(
            [Event(1, "stream-1", stale)],
            [Event(1, "snapshot-1", stale)]);

        var result = await CreateService(client).GetAsync(
            Instrument,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(MarketDataIntegrityStatus.RecoveryApplied, result.IntegrityStatus);
        Assert.False(result.IsFresh);
        Assert.Null(result.MarketEvent);
    }

    private static MarketSnapshotService CreateService(RecordingClient client) =>
        new(client, new FixedTimeProvider(Now));

    private static PaperMarketEvent Event(
        long sequence,
        string eventId,
        DateTimeOffset? timestamp = null)
    {
        var occurredAt = timestamp ?? Now;
        return new PaperMarketEvent(
            eventId,
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

    private sealed class RecordingClient(
        IEnumerable<PaperMarketEvent> streamEvents,
        IEnumerable<PaperMarketEvent> recoverySnapshots) : IMarketDataClient
    {
        private readonly Queue<PaperMarketEvent> _streamEvents = new(streamEvents);
        private readonly Queue<PaperMarketEvent> _recoverySnapshots = new(recoverySnapshots);

        public int RecoveryCalls { get; private set; }

        public ValueTask<PaperMarketEvent> GetTopOfBookAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_streamEvents.Dequeue());
        }

        public ValueTask<PaperMarketEvent> GetRecoverySnapshotAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoveryCalls++;
            return ValueTask.FromResult(_recoverySnapshots.Dequeue());
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
