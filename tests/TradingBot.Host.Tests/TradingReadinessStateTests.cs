namespace TradingBot.Host.Tests;

public sealed class TradingReadinessStateTests
{
    [Fact]
    public void OkxReadinessRequiresValidatedCandleHistory()
    {
        var readiness = new TradingReadinessState(candleHistoryRequired: true);

        readiness.MarkInstrumentReady("OKX:BTC-USDT");
        readiness.MarkMarketDataReady();

        Assert.False(readiness.Snapshot.IsReady);
        Assert.Equal("candle-history-not-ready", readiness.Snapshot.Reason);

        readiness.MarkCandleHistoryReady(timeframeSeconds: 900, warmupCandleCount: 200);

        Assert.True(readiness.Snapshot.IsReady);
        Assert.Null(readiness.Snapshot.Reason);
        Assert.Equal(900, readiness.Snapshot.CandleTimeframeSeconds);
        Assert.Equal(200, readiness.Snapshot.WarmupCandleCount);
    }

    [Fact]
    public void PaperReadinessDoesNotRequireCandleHistory()
    {
        var readiness = new TradingReadinessState();

        readiness.MarkInstrumentReady("PAPER:BTCUSDT");
        readiness.MarkMarketDataReady();

        Assert.True(readiness.Snapshot.IsReady);
        Assert.False(readiness.Snapshot.CandleHistoryRequired);
    }

    [Fact]
    public void LosingCandleHistoryClosesOkxReadiness()
    {
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        readiness.MarkInstrumentReady("OKX:BTC-USDT");
        readiness.MarkCandleHistoryReady(timeframeSeconds: 900, warmupCandleCount: 200);
        readiness.MarkMarketDataReady();

        readiness.MarkCandleHistoryNotReady("history-gap");

        Assert.False(readiness.Snapshot.IsReady);
        Assert.Equal("history-gap", readiness.Snapshot.Reason);
    }
}
