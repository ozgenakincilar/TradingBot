using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public readonly record struct AverageTrueRangeResult(
    int Period,
    int SampleCount,
    decimal Value);

public static class AverageTrueRange
{
    public static AverageTrueRangeResult Calculate(
        IReadOnlyList<Candle> candles,
        int period) => CalculateAt(candles, period, candles?.Count ?? 0);

    public static AverageTrueRangeResult CalculateAt(
        IReadOnlyList<Candle> candles,
        int period,
        int endExclusive)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (period < 2 || endExclusive > candles.Count ||
            endExclusive < checked(period + 1))
        {
            throw new DomainRuleViolationException(
                "ATR requires period plus one contiguous closed candles.");
        }

        var first = candles[0];
        decimal atr = 0m;
        try
        {
            for (var index = 1; index < endExclusive; index++)
            {
                var previous = candles[index - 1];
                var current = candles[index];
                if (current.InstrumentId != first.InstrumentId ||
                    current.Timeframe != first.Timeframe ||
                    current.OpenTime != previous.CloseTime)
                {
                    throw new DomainRuleViolationException(
                        "ATR input must be one contiguous closed-candle series.");
                }

                var trueRange = Math.Max(
                    checked(current.High - current.Low),
                    Math.Max(
                        Math.Abs(checked(current.High - previous.Close)),
                        Math.Abs(checked(current.Low - previous.Close))));
                if (index <= period)
                {
                    atr = checked(atr + trueRange);
                    if (index == period)
                    {
                        atr /= period;
                    }
                }
                else
                {
                    atr = checked((checked(atr * (period - 1m)) + trueRange) / period);
                }
            }
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("ATR calculation exceeded decimal bounds.");
        }

        return new AverageTrueRangeResult(period, endExclusive, atr);
    }
}
