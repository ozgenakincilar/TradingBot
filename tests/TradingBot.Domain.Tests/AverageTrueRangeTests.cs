using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Domain.Tests;

public sealed class AverageTrueRangeTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstantSeriesProducesZero()
    {
        var result = AverageTrueRange.Calculate(Series(15, 100m, 100m), 14);

        Assert.Equal(0m, result.Value);
        Assert.Equal(15, result.SampleCount);
    }

    [Fact]
    public void WiderRangeProducesLargerAtrAndIsDeterministic()
    {
        var narrow = AverageTrueRange.Calculate(Series(30, 100.5m, 99.5m), 14);
        var wideCandles = Series(30, 105m, 95m);
        var wide = AverageTrueRange.Calculate(wideCandles, 14);
        var repeated = AverageTrueRange.Calculate(wideCandles, 14);

        Assert.True(wide.Value > narrow.Value);
        Assert.Equal(wide, repeated);
    }

    [Fact]
    public void CalculateAtExcludesFutureCandle()
    {
        var candles = Series(16, 100.5m, 99.5m).ToArray();
        candles[^1] = Create(15, 150m, 50m);

        var previous = AverageTrueRange.CalculateAt(candles, 14, 15);
        var current = AverageTrueRange.Calculate(candles, 14);

        Assert.Equal(1m, previous.Value);
        Assert.True(current.Value > previous.Value);
    }

    [Fact]
    public void GapAndOverflowFailClosed()
    {
        var gap = Series(15, 101m, 99m).ToArray();
        gap[10] = Candle.CreateClosed(
            Instrument, Timeframe, Start + Timeframe.Duration * 11,
            Start.AddDays(2), 100m, 101m, 99m, 100m, 1m);
        var overflow = Enumerable.Range(0, 15)
            .Select(index => Candle.CreateClosed(
                Instrument, Timeframe, Start + Timeframe.Duration * index,
                Start.AddDays(2), 1m, decimal.MaxValue, 1m, 1m, 1m))
            .ToArray();

        Assert.Throws<DomainRuleViolationException>(
            () => AverageTrueRange.Calculate(gap, 14));
        Assert.Throws<DomainRuleViolationException>(
            () => AverageTrueRange.Calculate(overflow, 14));
    }

    private static IReadOnlyList<Candle> Series(int count, decimal high, decimal low) =>
        Enumerable.Range(0, count).Select(index => Create(index, high, low)).ToArray();

    private static Candle Create(int index, decimal high, decimal low) => Candle.CreateClosed(
        Instrument, Timeframe, Start + Timeframe.Duration * index,
        Start.AddDays(2), 100m, high, low, 100m, 1m);
}
