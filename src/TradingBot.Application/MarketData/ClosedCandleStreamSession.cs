using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public enum ClosedCandleStreamUpdateKind
{
    SessionReady = 1,
    Candle = 2
}

public sealed record ClosedCandleStreamUpdate(
    ClosedCandleStreamUpdateKind Kind,
    Candle? Candle)
{
    public static ClosedCandleStreamUpdate Ready() =>
        new(ClosedCandleStreamUpdateKind.SessionReady, null);

    public static ClosedCandleStreamUpdate FromCandle(Candle candle) =>
        new(ClosedCandleStreamUpdateKind.Candle, candle);
}

public sealed class ClosedCandleStreamSession(
    IClosedCandleStreamClient streamClient,
    IClosedCandleHistoryClient historyClient,
    TimeProvider timeProvider,
    int maximumCandlesPerRecovery)
{
    private const int BufferCapacity = 64;
    private const int MaximumTimeframeCount = 8;

    public async IAsyncEnumerable<ClosedCandleStreamUpdate> ReadValidatedAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requestedTimeframes = ValidateAndCopy(instrumentId, timeframes);
        if (maximumCandlesPerRecovery <= 0)
        {
            throw new DomainRuleViolationException(
                "Maximum candle stream recovery count must be positive.");
        }

        var channel = Channel.CreateBounded<Candle>(new BoundedChannelOptions(BufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var producer = PumpAsync(
            channel.Writer,
            streamClient,
            instrumentId,
            requestedTimeframes,
            sessionCancellation.Token);

        try
        {
            var guards = new Dictionary<Timeframe, ClosedCandleSequenceGuard>(
                requestedTimeframes.Length);
            var knownAt = timeProvider.GetUtcNow();
            var warmup = new WarmUpClosedCandles(
                historyClient,
                maximumCandlesPerRequest: maximumCandlesPerRecovery);
            foreach (var timeframe in requestedTimeframes)
            {
                var anchor = await warmup.HandleAsync(
                    instrumentId,
                    timeframe,
                    requiredCandleCount: 1,
                    knownAt,
                    cancellationToken);
                var guard = new ClosedCandleSequenceGuard(instrumentId, timeframe);
                var recovery = guard.ApplyRecovery(anchor.Candles);
                if (recovery.Status != ClosedCandleIntegrityStatus.RecoveryApplied ||
                    !recovery.IsReady)
                {
                    throw new DomainRuleViolationException(
                        "Closed candle stream anchor was not accepted.");
                }

                guards.Add(timeframe, guard);
            }

            yield return ClosedCandleStreamUpdate.Ready();

            await foreach (var candle in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (!guards.TryGetValue(candle.Timeframe, out var guard))
                {
                    throw new DomainRuleViolationException(
                        "Closed candle stream returned an unrequested timeframe.");
                }

                var observation = guard.Observe(candle);
                if (observation.Status is ClosedCandleIntegrityStatus.Duplicate or
                    ClosedCandleIntegrityStatus.OutOfOrder)
                {
                    continue;
                }

                if (observation.Status == ClosedCandleIntegrityStatus.Accepted)
                {
                    yield return ClosedCandleStreamUpdate.FromCandle(candle);
                    continue;
                }

                if (observation.Status != ClosedCandleIntegrityStatus.GapDetected ||
                    observation.ExpectedOpenTime is not { } expectedOpenTime)
                {
                    throw new DomainRuleViolationException(
                        $"Closed candle stream integrity failed with {observation.Status}.");
                }

                var gapRecovery = new RecoverClosedCandleGap(
                    historyClient,
                    maximumCandlesPerRecovery);
                var recovered = await gapRecovery.HandleAsync(
                    instrumentId,
                    candle.Timeframe,
                    expectedOpenTime,
                    candle.OpenTime,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                var applied = guard.ApplyRecovery(recovered);
                if (applied.Status != ClosedCandleIntegrityStatus.RecoveryApplied ||
                    !applied.IsReady)
                {
                    throw new DomainRuleViolationException(
                        "Closed candle stream gap recovery was rejected.");
                }

                foreach (var recoveredCandle in recovered)
                {
                    yield return ClosedCandleStreamUpdate.FromCandle(recoveredCandle);
                }
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

    private static Timeframe[] ValidateAndCopy(
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes)
    {
        ArgumentNullException.ThrowIfNull(timeframes);
        if (instrumentId == default ||
            timeframes.Count is < 1 or > MaximumTimeframeCount)
        {
            throw new DomainRuleViolationException(
                "Closed candle stream instrument and bounded timeframes are required.");
        }

        var copy = timeframes.ToArray();
        if (copy.Any(static timeframe => timeframe == default) ||
            copy.Distinct().Count() != copy.Length)
        {
            throw new DomainRuleViolationException(
                "Closed candle stream timeframes must be valid and unique.");
        }

        return copy;
    }

    private static async Task PumpAsync(
        ChannelWriter<Candle> writer,
        IClosedCandleStreamClient streamClient,
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var candle in streamClient.ReadClosedAsync(
                               instrumentId,
                               timeframes,
                               cancellationToken))
            {
                await writer.WriteAsync(candle, cancellationToken);
            }

            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
            throw;
        }
    }
}
