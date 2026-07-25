using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.MarketData;

public sealed record Candle
{
    private Candle(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal baseVolume)
    {
        InstrumentId = instrumentId;
        Timeframe = timeframe;
        OpenTime = openTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        BaseVolume = baseVolume;
    }

    public InstrumentId InstrumentId { get; }

    public Timeframe Timeframe { get; }

    public DateTimeOffset OpenTime { get; }

    public DateTimeOffset CloseTime => OpenTime + Timeframe.Duration;

    public decimal Open { get; }

    public decimal High { get; }

    public decimal Low { get; }

    public decimal Close { get; }

    public decimal BaseVolume { get; }

    public static Candle CreateClosed(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset openTime,
        DateTimeOffset knownAt,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal baseVolume)
    {
        if (instrumentId == default)
        {
            throw new DomainRuleViolationException("Candle instrument is required.");
        }

        if (timeframe == default)
        {
            throw new DomainRuleViolationException("Candle timeframe is required.");
        }

        Timeframe.EnsureUtc(openTime, nameof(openTime));
        Timeframe.EnsureUtc(knownAt, nameof(knownAt));
        if (!timeframe.IsBoundary(openTime))
        {
            throw new DomainRuleViolationException("Candle open time is not aligned to its UTC timeframe.");
        }

        var closeTime = openTime + timeframe.Duration;
        if (knownAt < closeTime)
        {
            throw new DomainRuleViolationException("An open candle cannot enter the closed-candle pipeline.");
        }

        if (open <= 0m || high <= 0m || low <= 0m || close <= 0m || baseVolume < 0m ||
            high < open || high < close || high < low ||
            low > open || low > close)
        {
            throw new DomainRuleViolationException("Candle OHLCV values are invalid.");
        }

        return new Candle(
            instrumentId,
            timeframe,
            openTime,
            open,
            high,
            low,
            close,
            baseVolume);
    }
}
