using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxBooks5MessageParserTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 7, 26, 2, 0, 0, TimeSpan.Zero);
    private readonly OkxBooks5MessageParser _parser = new();

    [Fact]
    public void Books5PayloadMapsSequenceContinuityAndTopOfBook()
    {
        var result = _parser.Parse(BookPayload(), Instrument, ReceivedAt);

        Assert.NotNull(result);
        Assert.Equal(3235851742, result.Sequence);
        Assert.Equal(3235851700, result.PreviousSequence);
        Assert.Equal(41006.3m, result.Snapshot.BestBid.Value);
        Assert.Equal(0.30178218m, result.Snapshot.BestBidQuantity);
        Assert.Equal(41006.8m, result.Snapshot.BestAsk.Value);
        Assert.Equal(0.60038921m, result.Snapshot.BestAskQuantity);
        Assert.True(result.Snapshot.HasDepth);
        Assert.Equal(2, result.Snapshot.BidDepth.Length);
        Assert.Equal(2, result.Snapshot.AskDepth.Length);
        Assert.Equal(41005.9m, result.Snapshot.BidDepth[1].Price.Value);
        Assert.Equal(41007.2m, result.Snapshot.AskDepth[1].Price.Value);
        Assert.Equal(ReceivedAt, result.ReceivedAt);
    }

    [Fact]
    public void SubscriptionAcknowledgementProducesNoMarketEvent()
    {
        var result = _parser.Parse(
            """{"event":"subscribe","arg":{"channel":"books5","instId":"BTC-USDT"},"connId":"id"}""",
            Instrument,
            ReceivedAt);

        Assert.Null(result);
    }

    [Fact]
    public void WebSocketErrorDoesNotExposeFreeTextMessage()
    {
        var action = () => _parser.Parse(
            """{"event":"error","code":"60012","msg":"sensitive server detail"}""",
            Instrument,
            ReceivedAt);

        var exception = Assert.Throws<DomainRuleViolationException>(action);
        Assert.Contains("60012", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CrossedOrderBookIsRejectedAtAdapterBoundary()
    {
        var crossed = BookPayload().Replace("41006.8", "41006.0", StringComparison.Ordinal);

        var action = () => _parser.Parse(crossed, Instrument, ReceivedAt);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static string BookPayload() => """
        {
          "arg":{"channel":"books5","instId":"BTC-USDT"},
          "action":"snapshot",
          "data":[{
            "asks":[
              ["41006.8","0.60038921","0","1"],
              ["41007.2","0.70000000","0","2"]
            ],
            "bids":[
              ["41006.3","0.30178218","0","2"],
              ["41005.9","0.40000000","0","1"]
            ],
            "ts":"1629966436396",
            "prevSeqId":3235851700,
            "seqId":3235851742
          }]
        }
        """;
}
