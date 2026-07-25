using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public sealed record ExponentialMovingAverageResult(
    int Period,
    int SampleCount,
    decimal Value);

public static class ExponentialMovingAverage
{
    public static ExponentialMovingAverageResult Calculate(
        IReadOnlyList<Candle> candles,
        int period)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (period <= 1 || candles.Count < period)
        {
            throw new DomainRuleViolationException(
                "EMA requires at least the complete configured period of closed candles.");
        }

        var start = candles.Count - period;
        var first = candles[start];
        var value = first.Close;
        var alpha = 2m / (period + 1m);

        try
        {
            for (var index = start + 1; index < candles.Count; index++)
            {
                var previous = candles[index - 1];
                var current = candles[index];
                if (current.InstrumentId != first.InstrumentId ||
                    current.Timeframe != first.Timeframe ||
                    current.OpenTime != previous.CloseTime)
                {
                    throw new DomainRuleViolationException(
                        "EMA input must be one contiguous closed-candle series.");
                }

                value = checked(value + (alpha * (current.Close - value)));
            }
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("EMA calculation exceeded decimal bounds.");
        }

        return new ExponentialMovingAverageResult(period, period, value);
    }
}
