using System.Collections.ObjectModel;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.MarketData;

public sealed class PagedClosedCandleHistoryClient : IClosedCandleHistoryClient
{
    private readonly IClosedCandleHistoryClient _inner;
    private readonly int _maximumPageSize;

    public PagedClosedCandleHistoryClient(
        IClosedCandleHistoryClient inner,
        int maximumPageSize)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (maximumPageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPageSize));
        }

        _inner = inner;
        _maximumPageSize = maximumPageSize;
    }

    public async ValueTask<IReadOnlyList<Candle>> GetAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        if (instrumentId == default || timeframe == default ||
            !timeframe.IsBoundary(fromInclusive) ||
            !timeframe.IsBoundary(toExclusive) ||
            toExclusive <= fromInclusive)
        {
            throw new DomainRuleViolationException("Paged candle history range is invalid.");
        }

        var duration = toExclusive - fromInclusive;
        if (duration.Ticks % timeframe.Duration.Ticks != 0)
        {
            throw new DomainRuleViolationException(
                "Paged candle history range must contain complete timeframe intervals.");
        }

        var remaining = duration.Ticks / timeframe.Duration.Ticks;
        var capacity = remaining > int.MaxValue ? 0 : (int)remaining;
        var result = capacity == 0 ? [] : new List<Candle>(capacity);
        var cursor = fromInclusive;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageCount = (int)Math.Min(_maximumPageSize, remaining);
            var pageEnd = AddCandles(cursor, timeframe, pageCount);
            var page = await _inner.GetAsync(
                instrumentId,
                timeframe,
                cursor,
                pageEnd,
                cancellationToken);
            if (page.Count != pageCount)
            {
                throw new DomainRuleViolationException(
                    "Paged candle history response did not cover its requested page.");
            }

            foreach (var candle in page)
            {
                if (candle is null || candle.InstrumentId != instrumentId ||
                    candle.Timeframe != timeframe || candle.OpenTime != cursor)
                {
                    throw new DomainRuleViolationException(
                        "Paged candle history response is not contiguous or has an invalid identity.");
                }

                result.Add(candle);
                cursor = candle.CloseTime;
                remaining--;
            }
        }

        if (cursor != toExclusive)
        {
            throw new DomainRuleViolationException(
                "Paged candle history did not reach its requested boundary.");
        }

        return new ReadOnlyCollection<Candle>(result);
    }

    private static DateTimeOffset AddCandles(
        DateTimeOffset start,
        Timeframe timeframe,
        int count)
    {
        try
        {
            return start.AddTicks(checked(timeframe.Duration.Ticks * count));
        }
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "Paged candle history exceeded the supported time range.");
        }
    }
}
