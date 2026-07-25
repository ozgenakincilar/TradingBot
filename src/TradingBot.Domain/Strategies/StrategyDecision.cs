using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public enum StrategyAction
{
    Hold = 1,
    EnterLong = 2,
    ExitToFlat = 3
}

public sealed record StrategyDecision
{
    private StrategyDecision(
        string strategyId,
        int strategyVersion,
        StrategyAction action,
        DateTimeOffset evaluatedAt,
        DateTimeOffset signalCandleOpenTime,
        DateTimeOffset trendCandleOpenTime,
        string reasonCode)
    {
        StrategyId = strategyId;
        StrategyVersion = strategyVersion;
        Action = action;
        EvaluatedAt = evaluatedAt;
        SignalCandleOpenTime = signalCandleOpenTime;
        TrendCandleOpenTime = trendCandleOpenTime;
        ReasonCode = reasonCode;
    }

    public string StrategyId { get; }

    public int StrategyVersion { get; }

    public StrategyAction Action { get; }

    public DateTimeOffset EvaluatedAt { get; }

    public DateTimeOffset SignalCandleOpenTime { get; }

    public DateTimeOffset TrendCandleOpenTime { get; }

    public string ReasonCode { get; }

    public static StrategyDecision Create(
        StrategyDefinition definition,
        StrategyAction action,
        Candle signalCandle,
        Candle trendCandle,
        DateTimeOffset evaluatedAt,
        string reasonCode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(signalCandle);
        ArgumentNullException.ThrowIfNull(trendCandle);

        if (!Enum.IsDefined(action))
        {
            throw new DomainRuleViolationException("Strategy action is invalid.");
        }

        if (evaluatedAt == default || evaluatedAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Strategy evaluation time must be UTC.");
        }

        if (signalCandle.InstrumentId != definition.InstrumentId ||
            signalCandle.Timeframe != definition.SignalTimeframe ||
            trendCandle.InstrumentId != definition.InstrumentId ||
            trendCandle.Timeframe != definition.TrendTimeframe)
        {
            throw new DomainRuleViolationException(
                "Strategy candles do not match the versioned strategy definition.");
        }

        if (signalCandle.CloseTime > evaluatedAt ||
            trendCandle.CloseTime > signalCandle.CloseTime)
        {
            throw new DomainRuleViolationException(
                "Strategy evaluation cannot use open or future candle data.");
        }

        if (!IsValidReasonCode(reasonCode))
        {
            throw new DomainRuleViolationException(
                "Strategy reason code must contain 3-64 lowercase ASCII letters, digits, or hyphens.");
        }

        return new StrategyDecision(
            definition.StrategyId,
            definition.Version,
            action,
            evaluatedAt,
            signalCandle.OpenTime,
            trendCandle.OpenTime,
            reasonCode);
    }

    private static bool IsValidReasonCode(string? value) =>
        value is { Length: >= 3 and <= 64 } &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
