using System.Runtime.CompilerServices;
using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.Tests;

public sealed class MarketDataStreamSessionTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoverySnapshotAndContinuousStreamArePublishedInOrder()
    {
        var session = CreateSession([Event(150, 100), Event(225, 150)]);

        var events = new List<PaperMarketEvent>();
        await foreach (var marketEvent in session.ReadValidatedAsync(
                           Instrument,
                           TimeSpan.FromSeconds(5),
                           CancellationToken.None))
        {
            events.Add(marketEvent);
        }

        Assert.Equal([100L, 150L, 225L], events.Select(x => x.Sequence));
    }

    [Fact]
    public async Task SequenceGapTerminatesSessionFailClosed()
    {
        var session = CreateSession([Event(225, 150)]);

        var action = async () =>
        {
            await foreach (var _ in session.ReadValidatedAsync(
                               Instrument,
                               TimeSpan.FromSeconds(5),
                               CancellationToken.None))
            {
            }
        };

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private static MarketDataStreamSession CreateSession(IReadOnlyCollection<PaperMarketEvent> events) =>
        new(
            new StubStreamClient(events),
            new StubSnapshotClient(Event(100, null)),
            new FixedTimeProvider(Now));

    private static PaperMarketEvent Event(long sequence, long? previousSequence) =>
        new(
            $"event-{sequence}",
            sequence,
            Now,
            new PaperTopOfBookSnapshot(
                Instrument,
                Price.From(99m),
                1m,
                Price.From(100m),
                1m,
                Now),
            previousSequence);

    private sealed class StubStreamClient(IReadOnlyCollection<PaperMarketEvent> events) :
        IMarketDataStreamClient
    {
        public async IAsyncEnumerable<PaperMarketEvent> ReadTopOfBookAsync(
            InstrumentId instrumentId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var marketEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return marketEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class StubSnapshotClient(PaperMarketEvent snapshot) : IMarketDataSnapshotClient
    {
        public ValueTask<PaperMarketEvent> GetRecoverySnapshotAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
