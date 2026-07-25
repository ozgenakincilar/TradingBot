using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public enum StrategyExposurePolicy
{
    LongFlat = 1
}

public sealed record StrategyDefinition
{
    private StrategyDefinition(
        string strategyId,
        int version,
        InstrumentId instrumentId,
        Timeframe signalTimeframe,
        Timeframe trendTimeframe,
        int signalEmaPeriod,
        int trendEmaPeriod,
        decimal maximumSignalCandleMovePercent,
        int minimumSignalWarmupCandles,
        int minimumTrendWarmupCandles)
    {
        StrategyId = strategyId;
        Version = version;
        InstrumentId = instrumentId;
        SignalTimeframe = signalTimeframe;
        TrendTimeframe = trendTimeframe;
        SignalEmaPeriod = signalEmaPeriod;
        TrendEmaPeriod = trendEmaPeriod;
        MaximumSignalCandleMovePercent = maximumSignalCandleMovePercent;
        MinimumSignalWarmupCandles = minimumSignalWarmupCandles;
        MinimumTrendWarmupCandles = minimumTrendWarmupCandles;
    }

    public string StrategyId { get; }

    public int Version { get; }

    public InstrumentId InstrumentId { get; }

    public StrategyExposurePolicy ExposurePolicy => StrategyExposurePolicy.LongFlat;

    public Timeframe SignalTimeframe { get; }

    public Timeframe TrendTimeframe { get; }

    public int SignalEmaPeriod { get; }

    public int TrendEmaPeriod { get; }

    public decimal MaximumSignalCandleMovePercent { get; }

    public int MinimumSignalWarmupCandles { get; }

    public int MinimumTrendWarmupCandles { get; }

    public static StrategyDefinition Create(
        string strategyId,
        int version,
        InstrumentId instrumentId,
        Timeframe signalTimeframe,
        Timeframe trendTimeframe,
        int signalEmaPeriod,
        int trendEmaPeriod,
        decimal maximumSignalCandleMovePercent,
        int minimumSignalWarmupCandles,
        int minimumTrendWarmupCandles)
    {
        if (!IsValidStrategyId(strategyId))
        {
            throw new DomainRuleViolationException(
                "Strategy id must contain 3-64 lowercase ASCII letters, digits, or hyphens.");
        }

        if (version <= 0 || instrumentId == default ||
            signalTimeframe == default || trendTimeframe == default)
        {
            throw new DomainRuleViolationException(
                "Strategy version, instrument, and timeframes are required.");
        }

        if (signalTimeframe.Duration >= trendTimeframe.Duration ||
            trendTimeframe.Duration.Ticks % signalTimeframe.Duration.Ticks != 0)
        {
            throw new DomainRuleViolationException(
                "Trend timeframe must be a larger exact multiple of the signal timeframe.");
        }

        if (signalEmaPeriod <= 1 || trendEmaPeriod <= 1 ||
            minimumSignalWarmupCandles <= signalEmaPeriod ||
            minimumTrendWarmupCandles < trendEmaPeriod)
        {
            throw new DomainRuleViolationException(
                "Strategy warm-up must cover the signal crossover and complete trend EMA period.");
        }

        if (maximumSignalCandleMovePercent <= 0m || maximumSignalCandleMovePercent > 10m)
        {
            throw new DomainRuleViolationException(
                "Maximum signal candle move must be greater than zero and at most ten percent.");
        }

        return new StrategyDefinition(
            strategyId,
            version,
            instrumentId,
            signalTimeframe,
            trendTimeframe,
            signalEmaPeriod,
            trendEmaPeriod,
            maximumSignalCandleMovePercent,
            minimumSignalWarmupCandles,
            minimumTrendWarmupCandles);
    }

    private static bool IsValidStrategyId(string? value) =>
        value is { Length: >= 3 and <= 64 } &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
