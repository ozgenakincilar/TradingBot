using System.Collections.ObjectModel;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public sealed class RecoverClosedCandleGap(
    IClosedCandleHistoryClient historyClient,
    int maximumCandlesPerRecovery)
{
    public async ValueTask<IReadOnlyList<Candle>> HandleAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset expectedOpenTime,
        DateTimeOffset observedOpenTime,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        if (instrumentId == default || timeframe == default)
        {
            throw new DomainRuleViolationException("Gap recovery instrument and timeframe are required.");
        }

        if (maximumCandlesPerRecovery <= 0)
        {
            throw new DomainRuleViolationException("Maximum candle recovery count must be positive.");
        }

        if (!timeframe.IsBoundary(expectedOpenTime) ||
            !timeframe.IsBoundary(observedOpenTime) ||
            observedOpenTime < expectedOpenTime)
        {
            throw new DomainRuleViolationException("Candle recovery range is invalid.");
        }

        if (knownAt == default || knownAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Candle recovery knowledge time must be UTC.");
        }
        var distance = observedOpenTime - expectedOpenTime;
        var count = checked((distance.Ticks / timeframe.Duration.Ticks) + 1L);
        if (distance.Ticks % timeframe.Duration.Ticks != 0 ||
            count > maximumCandlesPerRecovery)
        {
            throw new DomainRuleViolationException("Candle recovery range exceeds its bounded policy.");
        }

        var toExclusive = observedOpenTime + timeframe.Duration;
        if (knownAt < toExclusive)
        {
            throw new DomainRuleViolationException("Candle recovery cannot request an open candle.");
        }

        var received = await historyClient.GetAsync(
            instrumentId,
            timeframe,
            expectedOpenTime,
            toExclusive,
            cancellationToken);
        if (received is null || received.Count != count)
        {
            throw new DomainRuleViolationException("Candle recovery response did not cover the complete gap.");
        }

        var recovered = new Candle[received.Count];
        var expected = expectedOpenTime;
        for (var index = 0; index < received.Count; index++)
        {
            var candle = received[index];
            if (candle is null ||
                candle.InstrumentId != instrumentId ||
                candle.Timeframe != timeframe ||
                candle.OpenTime != expected ||
                candle.CloseTime > knownAt)
            {
                throw new DomainRuleViolationException("Candle recovery response was not contiguous and closed.");
            }

            recovered[index] = candle;
            expected = candle.CloseTime;
        }

        return new ReadOnlyCollection<Candle>(recovered);
    }
}
