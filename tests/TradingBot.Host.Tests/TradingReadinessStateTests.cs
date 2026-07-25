namespace TradingBot.Host.Tests;

public sealed class TradingReadinessStateTests
{
    [Fact]
    public void OkxReadinessRequiresBothValidatedCandleHistories()
    {
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        readiness.MarkInstrumentReady("OKX:BTC-USDT");
        readiness.MarkMarketDataReady();

        readiness.MarkSignalCandleHistoryReady(timeframeSeconds: 900, warmupCandleCount: 200);

        Assert.False(readiness.Snapshot.IsReady);
        Assert.False(readiness.Snapshot.CandleHistoryReady);
        Assert.Equal("candle-history-not-ready", readiness.Snapshot.Reason);

        readiness.MarkTrendCandleHistoryReady(timeframeSeconds: 3600, warmupCandleCount: 200);

        var snapshot = readiness.Snapshot;
        Assert.True(snapshot.IsReady);
        Assert.True(snapshot.CandleHistoryReady);
        Assert.Null(snapshot.Reason);
        Assert.Equal(900, snapshot.SignalCandleTimeframeSeconds);
        Assert.Equal(200, snapshot.SignalWarmupCandleCount);
        Assert.Equal(3600, snapshot.TrendCandleTimeframeSeconds);
        Assert.Equal(200, snapshot.TrendWarmupCandleCount);
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
    public void LosingEitherCandleHistoryClosesOkxReadiness()
    {
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        readiness.MarkInstrumentReady("OKX:BTC-USDT");
        readiness.MarkSignalCandleHistoryReady(timeframeSeconds: 900, warmupCandleCount: 200);
        readiness.MarkTrendCandleHistoryReady(timeframeSeconds: 3600, warmupCandleCount: 200);
        readiness.MarkMarketDataReady();

        readiness.MarkTrendCandleHistoryNotReady("trend-history-gap");

        Assert.False(readiness.Snapshot.IsReady);
        Assert.False(readiness.Snapshot.CandleHistoryReady);
        Assert.Equal("trend-history-gap", readiness.Snapshot.Reason);
    }
}
