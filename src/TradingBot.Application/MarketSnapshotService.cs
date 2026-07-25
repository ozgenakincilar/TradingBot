using System.Collections.Concurrent;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application;

public sealed record MarketSnapshotReadResult(
    PaperMarketEvent? MarketEvent,
    MarketDataIntegrityStatus IntegrityStatus,
    bool IsFresh);

public sealed class MarketSnapshotService(
    IMarketDataClient marketDataClient,
    TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<InstrumentId, IntegrityEntry> _integrity = new();

    public async ValueTask<MarketSnapshotReadResult> GetAsync(
        InstrumentId instrumentId,
        TimeSpan maximumAge,
        CancellationToken cancellationToken) =>
        await ExecuteSerializedAsync(
            instrumentId,
            maximumAge,
            cancellationToken);

    private async ValueTask<MarketSnapshotReadResult> ExecuteSerializedAsync(
        InstrumentId instrumentId,
        TimeSpan maximumAge,
        CancellationToken cancellationToken)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        var entry = _integrity.GetOrAdd(
            instrumentId,
            static key => new IntegrityEntry(new MarketDataIntegrityGuard(key)));
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            var marketEvent = await marketDataClient.GetTopOfBookAsync(
                instrumentId,
                cancellationToken);
            var observation = entry.Guard.Observe(ToCursor(marketEvent));
            if (observation.Status is MarketDataIntegrityStatus.Duplicate or
                MarketDataIntegrityStatus.OutOfOrder)
            {
                return new MarketSnapshotReadResult(null, observation.Status, entry.Guard.IsFresh(
                    timeProvider.GetUtcNow(), maximumAge));
            }

            if (observation.Status == MarketDataIntegrityStatus.Accepted)
            {
                return FreshResult(entry.Guard, marketEvent, observation.Status, maximumAge);
            }

            var recovery = await marketDataClient.GetRecoverySnapshotAsync(
                instrumentId,
                cancellationToken);
            var recovered = entry.Guard.ApplyRecoverySnapshot(ToCursor(recovery));
            if (recovered.Status != MarketDataIntegrityStatus.RecoveryApplied)
            {
                throw new DomainRuleViolationException("Market data recovery snapshot was rejected.");
            }

            return FreshResult(entry.Guard, recovery, recovered.Status, maximumAge);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private MarketSnapshotReadResult FreshResult(
        MarketDataIntegrityGuard guard,
        PaperMarketEvent marketEvent,
        MarketDataIntegrityStatus status,
        TimeSpan maximumAge)
    {
        var isFresh = guard.IsFresh(timeProvider.GetUtcNow(), maximumAge);
        return new MarketSnapshotReadResult(isFresh ? marketEvent : null, status, isFresh);
    }

    private static MarketDataCursor ToCursor(PaperMarketEvent marketEvent) =>
        new(
            marketEvent.Snapshot.InstrumentId,
            marketEvent.EventId,
            marketEvent.Sequence,
            marketEvent.Snapshot.OccurredAt,
            marketEvent.ReceivedAt);

    private sealed record IntegrityEntry(
        MarketDataIntegrityGuard Guard)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
