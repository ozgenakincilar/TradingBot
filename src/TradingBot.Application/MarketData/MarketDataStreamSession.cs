using System.Runtime.CompilerServices;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public enum MarketDataRecoveryMode
{
    RestSnapshotSequence = 1,
    EveryStreamEventIsSnapshot = 2
}

public sealed class MarketDataStreamSession(
    IMarketDataStreamClient streamClient,
    IMarketDataSnapshotClient snapshotClient,
    TimeProvider timeProvider,
    MarketDataRecoveryMode recoveryMode = MarketDataRecoveryMode.RestSnapshotSequence)
{
    private const int BufferCapacity = 1_024;

    public async IAsyncEnumerable<PaperMarketEvent> ReadValidatedAsync(
        InstrumentId instrumentId,
        TimeSpan maximumAge,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        var buffer = new MarketDataEventBuffer(BufferCapacity);
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = PumpAsync(buffer, instrumentId, streamClient, sessionCancellation.Token);
        try
        {
            var snapshot = await snapshotClient.GetRecoverySnapshotAsync(
                instrumentId,
                cancellationToken);
            var restGuard = new MarketDataIntegrityGuard(instrumentId);
            restGuard.ApplyRecoverySnapshot(ToCursor(snapshot));
            if (!restGuard.IsFresh(timeProvider.GetUtcNow(), maximumAge))
            {
                throw new DomainRuleViolationException("Market data recovery snapshot is stale.");
            }

            var sequenceAnchor = recoveryMode == MarketDataRecoveryMode.EveryStreamEventIsSnapshot
                ? await ReadFirstStreamEventAsync(buffer, cancellationToken)
                : snapshot;
            var buffered = new List<PaperMarketEvent>();
            while (buffer.TryRead(out var marketEvent))
            {
                buffered.Add(marketEvent!);
            }

            var replay = recoveryMode == MarketDataRecoveryMode.EveryStreamEventIsSnapshot
                ? new MarketDataReplayResult(
                    MarketDataReplayStatus.Aligned,
                    buffered.Where(candidate => candidate.Sequence > sequenceAnchor.Sequence).ToArray(),
                    buffered.Count == 0 ? sequenceAnchor.Sequence : buffered[^1].Sequence)
                : new MarketDataReplayAligner().Align(sequenceAnchor, buffered);
            if (replay.Status != MarketDataReplayStatus.Aligned)
            {
                throw new DomainRuleViolationException(
                    $"Market data replay failed with {replay.Status}.");
            }

            var guard = new MarketDataIntegrityGuard(instrumentId);
            guard.ApplyRecoverySnapshot(ToCursor(sequenceAnchor));
            if (!guard.IsFresh(timeProvider.GetUtcNow(), maximumAge))
            {
                throw new DomainRuleViolationException("Market data sequence anchor is stale.");
            }

            yield return sequenceAnchor;
            foreach (var replayEvent in replay.Events)
            {
                EnsureAcceptedAndFresh(guard, replayEvent, maximumAge, recoveryMode);
                yield return replayEvent;
            }

            await foreach (var marketEvent in buffer.ReadAllAsync(cancellationToken))
            {
                var observation = recoveryMode == MarketDataRecoveryMode.EveryStreamEventIsSnapshot
                    ? guard.ApplyRecoverySnapshot(ToCursor(marketEvent))
                    : guard.Observe(ToCursor(marketEvent));
                if (observation.Status is MarketDataIntegrityStatus.Duplicate or
                    MarketDataIntegrityStatus.OutOfOrder)
                {
                    continue;
                }

                if (observation.Status is not (MarketDataIntegrityStatus.Accepted or
                    MarketDataIntegrityStatus.RecoveryApplied))
                {
                    throw new DomainRuleViolationException(
                        $"Market stream integrity failed with {observation.Status}.");
                }

                if (!guard.IsFresh(timeProvider.GetUtcNow(), maximumAge))
                {
                    throw new DomainRuleViolationException("Market stream event is stale.");
                }

                yield return marketEvent;
            }
        }
        finally
        {
            sessionCancellation.Cancel();
            try
            {
                await producer;
            }
            catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private static async ValueTask<PaperMarketEvent> ReadFirstStreamEventAsync(
        MarketDataEventBuffer buffer,
        CancellationToken cancellationToken)
    {
        if (buffer.TryRead(out var marketEvent))
        {
            return marketEvent!;
        }

        return await buffer.ReadAsync(cancellationToken);
    }

    private void EnsureAcceptedAndFresh(
        MarketDataIntegrityGuard guard,
        PaperMarketEvent marketEvent,
        TimeSpan maximumAge,
        MarketDataRecoveryMode mode)
    {
        var observation = mode == MarketDataRecoveryMode.EveryStreamEventIsSnapshot
            ? guard.ApplyRecoverySnapshot(ToCursor(marketEvent))
            : guard.Observe(ToCursor(marketEvent));
        if (observation.Status is not (MarketDataIntegrityStatus.Accepted or
                MarketDataIntegrityStatus.RecoveryApplied) ||
            !guard.IsFresh(timeProvider.GetUtcNow(), maximumAge))
        {
            throw new DomainRuleViolationException("Buffered market event failed replay validation.");
        }
    }

    private static async Task PumpAsync(
        MarketDataEventBuffer buffer,
        InstrumentId instrumentId,
        IMarketDataStreamClient streamClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var marketEvent in streamClient.ReadTopOfBookAsync(
                               instrumentId,
                               cancellationToken))
            {
                await buffer.WriteAsync(marketEvent, cancellationToken);
            }

            buffer.Complete();
        }
        catch (Exception exception)
        {
            buffer.Complete(exception);
            throw;
        }
    }

    private static MarketDataCursor ToCursor(PaperMarketEvent marketEvent) =>
        new(
            marketEvent.Snapshot.InstrumentId,
            marketEvent.EventId,
            marketEvent.Sequence,
            marketEvent.Snapshot.OccurredAt,
            marketEvent.ReceivedAt,
            marketEvent.PreviousSequence);
}
