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
        StrategyPositionState position) => Evaluate(
            definition,
            signalCandles,
            trendCandles,
            position,
            StrategyTradeContext.None);

    public static StrategyDecision Evaluate(
        StrategyDefinition definition,
        IReadOnlyList<Candle> signalCandles,
        IReadOnlyList<Candle> trendCandles,
        StrategyPositionState position,
        StrategyTradeContext tradeContext)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(signalCandles);
        ArgumentNullException.ThrowIfNull(trendCandles);
        ArgumentNullException.ThrowIfNull(tradeContext);
        if (!Enum.IsDefined(position) ||
            signalCandles.Count < definition.MinimumSignalWarmupCandles ||
            trendCandles.Count < definition.MinimumTrendWarmupCandles)
        {
            throw new DomainRuleViolationException(
                "Strategy evaluation requires a valid position and complete warm-up series.");
        }

        ValidateTradeContext(definition, position, tradeContext);

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
        var hysteresisFraction = definition.SignalEmaHysteresisBasisPoints / 10_000m;
        var previousUpperBand = Multiply(previousSignalEma.Value, 1m + hysteresisFraction);
        var currentUpperBand = Multiply(currentSignalEma.Value, 1m + hysteresisFraction);
        var previousLowerBand = Multiply(previousSignalEma.Value, 1m - hysteresisFraction);
        var currentLowerBand = Multiply(currentSignalEma.Value, 1m - hysteresisFraction);
        var crossedUp = previousSignal.Close <= previousUpperBand &&
                        signal.Close > currentUpperBand;
        var crossedDown = previousSignal.Close >= previousLowerBand &&
                          signal.Close < currentLowerBand;
        var crossUpReason = definition.SignalEmaHysteresisBasisPoints == 0m
            ? "signal-ema-cross-up"
            : "signal-ema-hysteresis-cross-up";
        var crossDownReason = definition.SignalEmaHysteresisBasisPoints == 0m
            ? "signal-ema-cross-down"
            : "signal-ema-hysteresis-cross-down";

        StrategyAction action;
        string reason;
        if (position == StrategyPositionState.Long)
        {
            (action, reason) = !trendFilter.IsLongAllowed
                ? (StrategyAction.ExitToFlat, "trend-filter-exit")
                : IsProfitProtectionTriggered(definition, signal.Close, tradeContext)
                    ? (StrategyAction.ExitToFlat, "profit-protection-exit")
                : crossedDown
                    ? (StrategyAction.ExitToFlat, crossDownReason)
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
        else if (tradeContext.CompletedCandlesSinceExit is { } completed &&
                 completed < definition.ReentryCooldownCandles)
        {
            (action, reason) = (StrategyAction.Hold, "reentry-cooldown-blocked");
        }
        else if (GetPositiveBodyMovePercent(signal) > definition.MaximumSignalCandleMovePercent)
        {
            (action, reason) = (StrategyAction.Hold, "fomo-guard-blocked");
        }
        else if (definition.Version == 4 &&
                 AverageDirectionalIndex.Calculate(
                     trendCandles,
                     definition.TrendStrengthPeriod).MeetsMinimum(
                         definition.MinimumTrendStrength) is false)
        {
            (action, reason) = (StrategyAction.Hold, "trend-strength-blocked");
        }
        else
        {
            (action, reason) = (StrategyAction.EnterLong, crossUpReason);
        }

        return StrategyDecision.Create(
            definition,
            action,
            signal,
            trend,
            signal.CloseTime,
            reason);
    }

    private static bool IsProfitProtectionTriggered(
        StrategyDefinition definition,
        decimal currentClose,
        StrategyTradeContext context)
    {
        if (definition.Version != 3)
        {
            return false;
        }

        var entry = context.EntrySignalClose!.Value;
        var peak = Math.Max(context.PeakClose!.Value, currentClose);
        var activation = Multiply(
            entry,
            1m + definition.ProfitProtectionActivationBasisPoints / 10_000m);
        var trailingFloor = Multiply(
            peak,
            1m - definition.ProfitProtectionTrailingBasisPoints / 10_000m);
        return peak >= activation && currentClose <= trailingFloor;
    }

    private static void ValidateTradeContext(
        StrategyDefinition definition,
        StrategyPositionState position,
        StrategyTradeContext context)
    {
        if (definition.Version != 3)
        {
            return;
        }

        var valid = position switch
        {
            StrategyPositionState.Long =>
                context.EntrySignalClose is > 0m &&
                context.PeakClose >= context.EntrySignalClose &&
                context.CompletedCandlesSinceExit is null,
            StrategyPositionState.Flat =>
                context.EntrySignalClose is null &&
                context.PeakClose is null &&
                context.CompletedCandlesSinceExit is null or >= 0,
            _ => false
        };
        if (!valid)
        {
            throw new DomainRuleViolationException(
                "Strategy v3 trade context must match the current position.");
        }
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

    private static decimal Multiply(decimal left, decimal right)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Signal EMA hysteresis band exceeded decimal bounds.");
        }
    }
}
