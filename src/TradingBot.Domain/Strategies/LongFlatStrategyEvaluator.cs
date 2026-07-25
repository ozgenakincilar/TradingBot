using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public enum StrategyPositionState
{
    Flat = 1,
    Long = 2
}

public static class LongFlatStrategyEvaluator
{
    public static StrategyDecision Evaluate(
        StrategyDefinition definition,
        IReadOnlyList<Candle> signalCandles,
        IReadOnlyList<Candle> trendCandles,
        StrategyPositionState position)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(signalCandles);
        ArgumentNullException.ThrowIfNull(trendCandles);
        if (!Enum.IsDefined(position) ||
            signalCandles.Count < definition.MinimumSignalWarmupCandles ||
            trendCandles.Count < definition.MinimumTrendWarmupCandles)
        {
            throw new DomainRuleViolationException(
                "Strategy evaluation requires a valid position and complete warm-up series.");
        }

        var signal = signalCandles[^1];
        var trend = trendCandles[^1];
        if (trend.CloseTime > signal.CloseTime)
        {
            throw new DomainRuleViolationException("Future trend data cannot enter strategy evaluation.");
        }

        var trendFilter = EmaTrendFilter.Evaluate(definition, trendCandles);
        var previousSignalEma = ExponentialMovingAverage.CalculateAt(
            signalCandles,
            definition.SignalEmaPeriod,
            signalCandles.Count - 1);
        var currentSignalEma = ExponentialMovingAverage.Calculate(
            signalCandles,
            definition.SignalEmaPeriod);
        var previousSignal = signalCandles[^2];
        var crossedUp = previousSignal.Close <= previousSignalEma.Value &&
                        signal.Close > currentSignalEma.Value;
        var crossedDown = previousSignal.Close >= previousSignalEma.Value &&
                          signal.Close < currentSignalEma.Value;

        StrategyAction action;
        string reason;
        if (position == StrategyPositionState.Long)
        {
            (action, reason) = !trendFilter.IsLongAllowed
                ? (StrategyAction.ExitToFlat, "trend-filter-exit")
                : crossedDown
                    ? (StrategyAction.ExitToFlat, "signal-ema-cross-down")
                    : (StrategyAction.Hold, "long-position-held");
        }
        else if (!trendFilter.IsLongAllowed)
        {
            (action, reason) = (StrategyAction.Hold, "trend-filter-blocked");
        }
        else if (!crossedUp)
        {
            (action, reason) = (StrategyAction.Hold, "no-entry-signal");
        }
        else if (GetPositiveBodyMovePercent(signal) > definition.MaximumSignalCandleMovePercent)
        {
            (action, reason) = (StrategyAction.Hold, "fomo-guard-blocked");
        }
        else
        {
            (action, reason) = (StrategyAction.EnterLong, "signal-ema-cross-up");
        }

        return StrategyDecision.Create(
            definition,
            action,
            signal,
            trend,
            signal.CloseTime,
            reason);
    }

    private static decimal GetPositiveBodyMovePercent(Candle candle)
    {
        if (candle.Close <= candle.Open)
        {
            return 0m;
        }

        try
        {
            return checked(((candle.Close - candle.Open) / candle.Open) * 100m);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Signal candle move exceeded decimal bounds.");
        }
    }
}
