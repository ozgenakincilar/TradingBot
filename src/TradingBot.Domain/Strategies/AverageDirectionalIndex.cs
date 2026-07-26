using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public sealed record AverageDirectionalIndexResult(
    int Period,
    int SampleCount,
    decimal Value)
{
    public bool MeetsMinimum(decimal minimum) =>
        minimum is > 0m and <= 100m && Value >= minimum;
}

public static class AverageDirectionalIndex
{
    public static AverageDirectionalIndexResult Calculate(
        IReadOnlyList<Candle> candles,
        int period)
    {
        ArgumentNullException.ThrowIfNull(candles);
        if (period < 2 || candles.Count < checked(period * 2))
        {
            throw new DomainRuleViolationException(
                "ADX requires at least twice the configured period of contiguous closed candles.");
        }

        var first = candles[0];
        decimal smoothedTrueRange = 0m;
        decimal smoothedPlusDm = 0m;
        decimal smoothedMinusDm = 0m;
        decimal adx = 0m;
        var dxCount = 0;

        try
        {
            for (var index = 1; index < candles.Count; index++)
            {
                var previous = candles[index - 1];
                var current = candles[index];
                if (current.InstrumentId != first.InstrumentId ||
                    current.Timeframe != first.Timeframe ||
                    current.OpenTime != previous.CloseTime)
                {
                    throw new DomainRuleViolationException(
                        "ADX input must be one contiguous closed-candle series.");
                }

                var upMove = checked(current.High - previous.High);
                var downMove = checked(previous.Low - current.Low);
                var plusDm = upMove > downMove && upMove > 0m ? upMove : 0m;
                var minusDm = downMove > upMove && downMove > 0m ? downMove : 0m;
                var trueRange = Math.Max(
                    checked(current.High - current.Low),
                    Math.Max(
                        Math.Abs(checked(current.High - previous.Close)),
                        Math.Abs(checked(current.Low - previous.Close))));

                if (index <= period)
                {
                    smoothedTrueRange = checked(smoothedTrueRange + trueRange);
                    smoothedPlusDm = checked(smoothedPlusDm + plusDm);
                    smoothedMinusDm = checked(smoothedMinusDm + minusDm);
                }
                else
                {
                    smoothedTrueRange = Smooth(smoothedTrueRange, trueRange, period);
                    smoothedPlusDm = Smooth(smoothedPlusDm, plusDm, period);
                    smoothedMinusDm = Smooth(smoothedMinusDm, minusDm, period);
                }

                if (index < period)
                {
                    continue;
                }

                var dx = CalculateDx(smoothedTrueRange, smoothedPlusDm, smoothedMinusDm);
                dxCount++;
                if (dxCount <= period)
                {
                    adx = checked(adx + dx);
                    if (dxCount == period)
                    {
                        adx /= period;
                    }
                }
                else
                {
                    adx = checked((checked(adx * (period - 1m)) + dx) / period);
                }
            }
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("ADX calculation exceeded decimal bounds.");
        }

        return new AverageDirectionalIndexResult(period, candles.Count, adx);
    }

    private static decimal Smooth(decimal previous, decimal current, int period) =>
        checked(previous - previous / period + current);

    private static decimal CalculateDx(decimal trueRange, decimal plusDm, decimal minusDm)
    {
        if (trueRange == 0m)
        {
            return 0m;
        }

        var plusDi = checked(100m * plusDm / trueRange);
        var minusDi = checked(100m * minusDm / trueRange);
        var denominator = checked(plusDi + minusDi);
        return denominator == 0m
            ? 0m
            : checked(100m * Math.Abs(checked(plusDi - minusDi)) / denominator);
    }
}
