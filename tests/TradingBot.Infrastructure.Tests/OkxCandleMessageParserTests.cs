using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxCandleMessageParserTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe FifteenMinutes = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe OneHour = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly Timeframe[] ExpectedTimeframes = [FifteenMinutes, OneHour];
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 7, 25, 12, 20, 0, TimeSpan.Zero);
    private readonly OkxCandleMessageParser _parser = new();

    [Fact]
    public void ConfirmedCandleMapsToClosedDomainCandle()
    {
        var result = _parser.Parse(
            Payload("candle15m", "1"),
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.NotNull(result);
        Assert.Equal(FifteenMinutes, result.Timeframe);
        Assert.Equal(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), result.OpenTime);
        Assert.Equal(100m, result.Open);
        Assert.Equal(105m, result.Close);
        Assert.Equal(12m, result.BaseVolume);
    }

    [Fact]
    public void UnconfirmedCandleIsNotPublished()
    {
        var result = _parser.Parse(
            Payload("candle15m", "0"),
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.Null(result);
    }

    [Fact]
    public void OneHourChannelMapsToExpectedTimeframe()
    {
        var result = _parser.Parse(
            Payload("candle1H", "1", new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero)),
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.Equal(OneHour, result?.Timeframe);
    }

    [Fact]
    public void SubscriptionAcknowledgementProducesNoCandle()
    {
        var result = _parser.Parse(
            """{"event":"subscribe","arg":{"channel":"candle15m","instId":"BTC-USDT"}}""",
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.Null(result);
    }

    [Fact]
    public void UnexpectedChannelIsRejected()
    {
        var action = () => _parser.Parse(
            Payload("candle5m", "1"),
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void FutureClosedCandleIsRejected()
    {
        var action = () => _parser.Parse(
            Payload("candle15m", "1", ReceivedAt),
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void WebSocketErrorDoesNotExposeFreeTextMessage()
    {
        var action = () => _parser.Parse(
            """{"event":"error","code":"60012","msg":"sensitive detail"}""",
            Instrument,
            ExpectedTimeframes,
            ReceivedAt);

        var exception = Assert.Throws<DomainRuleViolationException>(action);
        Assert.Contains("60012", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string Payload(
        string channel,
        string confirm,
        DateTimeOffset? openTime = null) => $$"""
        {
          "arg":{"channel":"{{channel}}","instId":"BTC-USDT"},
          "data":[["{{(openTime ?? new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero)).ToUnixTimeMilliseconds()}}","100","110","90","105","12","1260","1260","{{confirm}}"]]
        }
        """;
}
