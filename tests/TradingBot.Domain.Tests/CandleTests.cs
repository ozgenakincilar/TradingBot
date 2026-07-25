using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Domain.Tests;

public sealed class CandleTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe OneMinute = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset OpenTime = new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClosedCandleUsesExclusiveUtcCloseBoundary()
    {
        var candle = Create(OpenTime, OpenTime.AddMinutes(1));

        Assert.Equal(OpenTime.AddMinutes(1), candle.CloseTime);
        Assert.Equal(100m, candle.Open);
        Assert.Equal(110m, candle.High);
        Assert.Equal(90m, candle.Low);
        Assert.Equal(105m, candle.Close);
        Assert.Equal(12m, candle.BaseVolume);
    }

    [Fact]
    public void CandleBeforeCloseIsRejected()
    {
        var action = () => Create(OpenTime, OpenTime.AddSeconds(59));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void CandleOutsideUtcBoundaryIsRejected()
    {
        var action = () => Create(OpenTime.AddSeconds(1), OpenTime.AddMinutes(2));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Theory]
    [InlineData(0, 110, 90, 105, 12)]
    [InlineData(100, 99, 90, 105, 12)]
    [InlineData(100, 110, 106, 105, 12)]
    [InlineData(100, 110, 90, 105, -1)]
    public void InvalidOhlcvIsRejected(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        var action = () => Candle.CreateClosed(
            Instrument,
            OneMinute,
            OpenTime,
            OpenTime.AddMinutes(1),
            open,
            high,
            low,
            close,
            volume);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void SubSecondTimeframeIsRejected()
    {
        var action = () =>
        {
            _ = Timeframe.Create(TimeSpan.FromMilliseconds(500));
        };

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void DefaultTimeframeCannotEvaluateBoundary()
    {
        var action = () =>
        {
            _ = default(Timeframe).IsBoundary(OpenTime);
        };

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void TimestampInsideCandleFloorsToUtcBoundary()
    {
        var timestamp = OpenTime.AddSeconds(42);

        var boundary = OneMinute.GetBoundaryAtOrBefore(timestamp);

        Assert.Equal(OpenTime, boundary);
    }

    [Fact]
    public void ExactBoundaryIsPreserved()
    {
        var boundary = OneMinute.GetBoundaryAtOrBefore(OpenTime);

        Assert.Equal(OpenTime, boundary);
    }

    [Fact]
    public void NonUtcTimestampCannotBeFloored()
    {
        var localOffset = new DateTimeOffset(2026, 7, 25, 13, 0, 0, TimeSpan.FromHours(3));
        var action = () =>
        {
            _ = OneMinute.GetBoundaryAtOrBefore(localOffset);
        };

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static Candle Create(DateTimeOffset openTime, DateTimeOffset knownAt) =>
        Candle.CreateClosed(
            Instrument,
            OneMinute,
            openTime,
            knownAt,
            100m,
            110m,
            90m,
            105m,
            12m);
}
