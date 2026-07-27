using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Domain.Tests;

public sealed class AverageDirectionalIndexTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Start =
        new(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstantSeriesProducesZeroWithoutDivisionByZero()
    {
        var result = AverageDirectionalIndex.Calculate(
            Series(28, static _ => 100m),
            14);

        Assert.Equal(0m, result.Value);
        Assert.Equal(28, result.SampleCount);
    }

    [Fact]
    public void CanonicalOneWaySeriesProducesExactHundred()
    {
        var result = AverageDirectionalIndex.Calculate(
            Series(28, static index => 100m + index),
            14);

        Assert.Equal(100m, result.Value);
        Assert.True(result.PlusDirectionalIndex > result.MinusDirectionalIndex);
    }

    [Fact]
    public void CanonicalFallingSeriesProducesNegativeDirectionalDominance()
    {
        var result = AverageDirectionalIndex.Calculate(
            Series(28, static index => 200m - index), 14);

        Assert.Equal(100m, result.Value);
        Assert.True(result.MinusDirectionalIndex > result.PlusDirectionalIndex);
    }

    [Fact]
    public void AlternatingRangeStaysBelowTrendThresholdAndIsDeterministic()
    {
        var candles = Series(200, static index => index % 2 == 0 ? 100m : 101m);

        var first = AverageDirectionalIndex.Calculate(candles, 14);
        var second = AverageDirectionalIndex.Calculate(candles, 14);

        Assert.True(first.Value < 25m);
        Assert.Equal(first, second);
    }

    [Fact]
    public void MinimumThresholdIsInclusive()
    {
        var result = new AverageDirectionalIndexResult(14, 28, 25m);

        Assert.True(result.MeetsMinimum(25m));
        Assert.False(result.MeetsMinimum(25.000000000000000000000000001m));
    }

    [Fact]
    public void FewerThanTwiceThePeriodFailsClosed()
    {
        var action = () => AverageDirectionalIndex.Calculate(
            Series(27, static index => 100m + index),
            14);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void GapFailsClosed()
    {
        var candles = Series(28, static index => 100m + index).ToArray();
        candles[20] = Create(21, 121m);

        var action = () => AverageDirectionalIndex.Calculate(candles, 14);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void MixedInstrumentFailsClosed()
    {
        var candles = Series(28, static index => 100m + index).ToArray();
        candles[20] = Candle.CreateClosed(
            InstrumentId.Create("OKX", "ETH-USDT"), Timeframe,
            Start + Timeframe.Duration * 20, Start + Timeframe.Duration * 1_000,
            120m, 120m, 120m, 120m, 1m);

        Assert.Throws<DomainRuleViolationException>(
            () => AverageDirectionalIndex.Calculate(candles, 14));
    }

    [Fact]
    public void DecimalOverflowFailsClosed()
    {
        var candles = Enumerable.Range(0, 28)
            .Select(index => Candle.CreateClosed(
                Instrument, Timeframe, Start + Timeframe.Duration * index,
                Start + Timeframe.Duration * 1_000,
                1m, decimal.MaxValue, 1m, 1m, 1m))
            .ToArray();

        Assert.Throws<DomainRuleViolationException>(
            () => AverageDirectionalIndex.Calculate(candles, 14));
    }

    private static IReadOnlyList<Candle> Series(int count, Func<int, decimal> close) =>
        Enumerable.Range(0, count).Select(index => Create(index, close(index))).ToArray();

    private static Candle Create(int index, decimal close) => Candle.CreateClosed(
        Instrument,
        Timeframe,
        Start + Timeframe.Duration * index,
        Start + Timeframe.Duration * 1_000,
        close,
        close,
        close,
        close,
        1m);
}
