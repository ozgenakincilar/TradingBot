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
        int minimumTrendWarmupCandles,
        decimal signalEmaHysteresisBasisPoints,
        int reentryCooldownCandles,
        decimal profitProtectionActivationBasisPoints,
        decimal profitProtectionTrailingBasisPoints,
        int trendStrengthPeriod,
        decimal minimumTrendStrength)
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
        SignalEmaHysteresisBasisPoints = signalEmaHysteresisBasisPoints;
        ReentryCooldownCandles = reentryCooldownCandles;
        ProfitProtectionActivationBasisPoints = profitProtectionActivationBasisPoints;
        ProfitProtectionTrailingBasisPoints = profitProtectionTrailingBasisPoints;
        TrendStrengthPeriod = trendStrengthPeriod;
        MinimumTrendStrength = minimumTrendStrength;
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

    public decimal SignalEmaHysteresisBasisPoints { get; }

    public int ReentryCooldownCandles { get; }

    public decimal ProfitProtectionActivationBasisPoints { get; }

    public decimal ProfitProtectionTrailingBasisPoints { get; }

    public int TrendStrengthPeriod { get; }

    public decimal MinimumTrendStrength { get; }

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
        int minimumTrendWarmupCandles,
        decimal signalEmaHysteresisBasisPoints = 0m,
        int reentryCooldownCandles = 0,
        decimal profitProtectionActivationBasisPoints = 0m,
        decimal profitProtectionTrailingBasisPoints = 0m,
        int trendStrengthPeriod = 0,
        decimal minimumTrendStrength = 0m)
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

        if (signalEmaHysteresisBasisPoints is < 0m or > 1_000m ||
            (version == 1 && signalEmaHysteresisBasisPoints != 0m))
        {
            throw new DomainRuleViolationException(
                "Signal EMA hysteresis must be zero for v1 and between zero and 1,000 basis points.");
        }

        var hasProfitProtection = reentryCooldownCandles != 0 ||
            profitProtectionActivationBasisPoints != 0m ||
            profitProtectionTrailingBasisPoints != 0m;
        if ((version != 3 && hasProfitProtection) ||
            (version == 3 &&
             (reentryCooldownCandles is < 1 or > 96 ||
              profitProtectionActivationBasisPoints is <= 0m or > 1_000m ||
              profitProtectionTrailingBasisPoints <= 0m ||
              profitProtectionTrailingBasisPoints >= profitProtectionActivationBasisPoints)))
        {
            throw new DomainRuleViolationException(
                "Profit protection must be disabled before v3; v3 requires a bounded cooldown and a trailing distance below its activation threshold.");
        }

        var hasTrendStrength = trendStrengthPeriod != 0 || minimumTrendStrength != 0m;
        if ((version != 4 && hasTrendStrength) ||
            (version == 4 &&
             (trendStrengthPeriod is < 2 or > 100 ||
              minimumTrendStrength is <= 0m or > 100m ||
              minimumTrendWarmupCandles < trendStrengthPeriod * 2)))
        {
            throw new DomainRuleViolationException(
                "Trend strength must be disabled outside v4; v4 requires a bounded period, threshold, and complete ADX warm-up.");
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
            minimumTrendWarmupCandles,
            signalEmaHysteresisBasisPoints,
            reentryCooldownCandles,
            profitProtectionActivationBasisPoints,
            profitProtectionTrailingBasisPoints,
            trendStrengthPeriod,
            minimumTrendStrength);
    }

    private static bool IsValidStrategyId(string? value) =>
        value is { Length: >= 3 and <= 64 } &&
        value[0] is >= 'a' and <= 'z' &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
}
