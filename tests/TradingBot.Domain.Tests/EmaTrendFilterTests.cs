using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Domain.Tests;

public sealed class EmaTrendFilterTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Start =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmaUsesOnlyLatestConfiguredWindowWithFirstCloseSeed()
    {
        var candles = Candles(50m, 10m, 20m, 30m);

        var result = ExponentialMovingAverage.Calculate(candles, period: 3);

        Assert.Equal(22.5m, result.Value);
        Assert.Equal(3, result.SampleCount);
    }

    [Theory]
    [InlineData("10,10,20", true)]
    [InlineData("10,10,10", false)]
    [InlineData("20,10,10", false)]
    public void TrendFilterRequiresCloseStrictlyAboveEma(string values, bool expected)
    {
        var closes = values.Split(',').Select(decimal.Parse).ToArray();
        var definition = StrategyDefinition.Create(
            "btc-long-flat",
            1,
            Instrument,
            Signal,
            Trend,
            signalEmaPeriod: 2,
            trendEmaPeriod: 3,
            maximumSignalCandleMovePercent: 2m,
            minimumSignalWarmupCandles: 3,
            minimumTrendWarmupCandles: 3);

        var result = EmaTrendFilter.Evaluate(definition, Candles(closes));

        Assert.Equal(expected, result.IsLongAllowed);
        Assert.Equal(closes[^1], result.LatestClose);
    }

    [Fact]
    public void EmaRejectsNonContiguousInput()
    {
        var candles = Candles(10m, 20m, 30m).ToArray();
        candles[1] = CandleAt(2, 20m);

        Assert.Throws<DomainRuleViolationException>(() =>
            ExponentialMovingAverage.Calculate(candles, period: 3));
    }

    private static IReadOnlyList<Candle> Candles(params decimal[] closes) =>
        closes.Select((close, index) => CandleAt(index, close)).ToArray();

    private static Candle CandleAt(int index, decimal close) =>
        Candle.CreateClosed(
            Instrument,
            Trend,
            Start + (Trend.Duration * index),
            Start + (Trend.Duration * 10),
            close,
            close,
            close,
            close,
            1m);
}
