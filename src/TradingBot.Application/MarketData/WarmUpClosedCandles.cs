using System.Collections.ObjectModel;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public sealed record ClosedCandleWarmupResult(
    InstrumentId InstrumentId,
    Timeframe Timeframe,
    DateTimeOffset FromInclusive,
    DateTimeOffset ToExclusive,
    IReadOnlyList<Candle> Candles);

public sealed class WarmUpClosedCandles
{
    private readonly IClosedCandleHistoryClient _historyClient;
    private readonly int _maximumCandlesPerRequest;

    public WarmUpClosedCandles(
        IClosedCandleHistoryClient historyClient,
        int maximumCandlesPerRequest)
    {
        ArgumentNullException.ThrowIfNull(historyClient);
        if (maximumCandlesPerRequest <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCandlesPerRequest));
        }

        _historyClient = historyClient;
        _maximumCandlesPerRequest = maximumCandlesPerRequest;
    }

    public async ValueTask<ClosedCandleWarmupResult> HandleAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        int requiredCandleCount,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (instrumentId == default || timeframe == default)
        {
            throw new DomainRuleViolationException("Candle warm-up instrument and timeframe are required.");
        }

        if (requiredCandleCount <= 0 || requiredCandleCount > _maximumCandlesPerRequest)
        {
            throw new DomainRuleViolationException("Candle warm-up count exceeds its bounded policy.");
        }

        var toExclusive = timeframe.GetBoundaryAtOrBefore(knownAt);
        DateTimeOffset fromInclusive;
        try
        {
            var lookbackTicks = checked(timeframe.Duration.Ticks * requiredCandleCount);
            fromInclusive = toExclusive - TimeSpan.FromTicks(lookbackTicks);
        }
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException("Candle warm-up range is outside the supported timeline.");
        }

        var received = await _historyClient.GetAsync(
            instrumentId,
            timeframe,
            fromInclusive,
            toExclusive,
            cancellationToken);
        if (received is null || received.Count != requiredCandleCount)
        {
            throw new DomainRuleViolationException("Candle warm-up did not return the required history.");
        }

        var candles = new Candle[received.Count];
        for (var index = 0; index < received.Count; index++)
        {
            candles[index] = received[index]
                ?? throw new DomainRuleViolationException("Candle warm-up returned an empty item.");
        }

        if (candles[0].OpenTime != fromInclusive ||
            candles[^1].CloseTime != toExclusive ||
            candles[^1].CloseTime > knownAt)
        {
            throw new DomainRuleViolationException("Candle warm-up boundaries are incomplete.");
        }

        var guard = new ClosedCandleSequenceGuard(instrumentId, timeframe);
        var integrity = guard.ApplyRecovery(candles);
        if (integrity.Status != ClosedCandleIntegrityStatus.RecoveryApplied || !integrity.IsReady)
        {
            throw new DomainRuleViolationException("Candle warm-up history is not contiguous.");
        }

        return new ClosedCandleWarmupResult(
            instrumentId,
            timeframe,
            fromInclusive,
            toExclusive,
            new ReadOnlyCollection<Candle>(candles));
    }
}
