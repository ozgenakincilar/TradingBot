using TradingBot.Application.Abstractions;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public enum MarketDataReplayStatus
{
    Aligned = 1,
    GapDetected = 2,
    ConflictingSequence = 3,
    TimestampRegression = 4
}

public sealed record MarketDataReplayResult(
    MarketDataReplayStatus Status,
    IReadOnlyCollection<PaperMarketEvent> Events,
    long LastAcceptedSequence);

public sealed class MarketDataReplayAligner
{
    public MarketDataReplayResult Align(
        PaperMarketEvent recoverySnapshot,
        IEnumerable<PaperMarketEvent> bufferedEvents)
    {
        ArgumentNullException.ThrowIfNull(recoverySnapshot);
        ArgumentNullException.ThrowIfNull(bufferedEvents);

        var guard = new MarketDataIntegrityGuard(recoverySnapshot.Snapshot.InstrumentId);
        var recovery = guard.ApplyRecoverySnapshot(ToCursor(recoverySnapshot));
        if (recovery.Status != MarketDataIntegrityStatus.RecoveryApplied)
        {
            throw new InvalidOperationException("Initial market data recovery snapshot was rejected.");
        }

        var aligned = new List<PaperMarketEvent>();
        var lastAcceptedSequence = recoverySnapshot.Sequence;
        foreach (var marketEvent in bufferedEvents)
        {
            ArgumentNullException.ThrowIfNull(marketEvent);
            if (marketEvent.Sequence <= recoverySnapshot.Sequence)
            {
                continue;
            }

            var observation = guard.Observe(ToCursor(marketEvent));
            switch (observation.Status)
            {
                case MarketDataIntegrityStatus.Accepted:
                    aligned.Add(marketEvent);
                    lastAcceptedSequence = marketEvent.Sequence;
                    break;
                case MarketDataIntegrityStatus.Duplicate:
                case MarketDataIntegrityStatus.OutOfOrder:
                    break;
                case MarketDataIntegrityStatus.GapDetected:
                    return Failed(MarketDataReplayStatus.GapDetected, lastAcceptedSequence);
                case MarketDataIntegrityStatus.ConflictingSequence:
                    return Failed(MarketDataReplayStatus.ConflictingSequence, lastAcceptedSequence);
                case MarketDataIntegrityStatus.TimestampRegression:
                    return Failed(MarketDataReplayStatus.TimestampRegression, lastAcceptedSequence);
                default:
                    throw new InvalidOperationException("Unexpected market data replay state.");
            }
        }

        return new MarketDataReplayResult(
            MarketDataReplayStatus.Aligned,
            aligned,
            lastAcceptedSequence);
    }

    private static MarketDataReplayResult Failed(
        MarketDataReplayStatus status,
        long lastAcceptedSequence) =>
        new(status, Array.Empty<PaperMarketEvent>(), lastAcceptedSequence);

    private static MarketDataCursor ToCursor(PaperMarketEvent marketEvent) =>
        new(
            marketEvent.Snapshot.InstrumentId,
            marketEvent.EventId,
            marketEvent.Sequence,
            marketEvent.Snapshot.OccurredAt,
            marketEvent.ReceivedAt,
            marketEvent.PreviousSequence);
}
