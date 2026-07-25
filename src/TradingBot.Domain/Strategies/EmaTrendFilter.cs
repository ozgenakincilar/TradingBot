using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Strategies;

public sealed record EmaTrendFilterResult(
    decimal LatestClose,
    decimal Ema,
    bool IsLongAllowed);

public static class EmaTrendFilter
{
    public static EmaTrendFilterResult Evaluate(
        StrategyDefinition definition,
        IReadOnlyList<Candle> trendCandles)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(trendCandles);
        if (trendCandles.Count < definition.MinimumTrendWarmupCandles)
        {
            throw new DomainRuleViolationException(
                "Trend filter requires the configured warm-up history.");
        }

        var latest = trendCandles[^1];
        if (latest.InstrumentId != definition.InstrumentId ||
            latest.Timeframe != definition.TrendTimeframe)
        {
            throw new DomainRuleViolationException(
                "Trend filter input does not match the strategy contract.");
        }

        var ema = ExponentialMovingAverage.Calculate(trendCandles, definition.TrendEmaPeriod);
        return new EmaTrendFilterResult(latest.Close, ema.Value, latest.Close > ema.Value);
    }
}
