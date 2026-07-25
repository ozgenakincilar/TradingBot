using TradingBot.Application.MarketData;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxPublicWebSocketConnectivityTests
{
    private const string ConnectivityVariable = "TRADINGBOT_RUN_OKX_CONNECTIVITY";

    [Fact]
    public async Task Books5StreamReceivesPublicSpotSnapshot()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ConnectivityVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var client = new OkxSpotMarketStreamClient(
            new Uri("wss://ws.okx.com:8443/ws/v5/public"),
            TimeProvider.System,
            new OkxBooks5MessageParser());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var stream = client.ReadTopOfBookAsync(
            InstrumentId.Create("OKX", "BTC-USDT"),
            timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await stream.MoveNextAsync());
        Assert.True(stream.Current.Sequence >= 0);
        Assert.True(stream.Current.Snapshot.BestBid.Value > 0m);
        Assert.True(stream.Current.Snapshot.BestAsk.Value >= stream.Current.Snapshot.BestBid.Value);
    }

    [Fact]
    public async Task RestSnapshotAndWebSocketReplayProduceValidatedSession()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ConnectivityVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var timeProvider = TimeProvider.System;
        var parser = new OkxBooks5MessageParser();
        var streamClient = new OkxSpotMarketStreamClient(
            new Uri("wss://ws.okx.com:8443/ws/v5/public"),
            timeProvider,
            parser);
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://www.okx.com/") };
        var snapshotClient = new OkxSpotMarketSnapshotClient(httpClient, timeProvider);
        var session = new MarketDataStreamSession(
            streamClient,
            snapshotClient,
            timeProvider,
            MarketDataRecoveryMode.EveryStreamEventIsSnapshot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sequences = new List<long>();

        await foreach (var marketEvent in session.ReadValidatedAsync(
                           InstrumentId.Create("OKX", "BTC-USDT"),
                           TimeSpan.FromSeconds(15),
                           timeout.Token))
        {
            sequences.Add(marketEvent.Sequence);
            if (sequences.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(2, sequences.Count);
    }

    [Fact]
    public async Task PublicInstrumentCatalogReturnsLiveSpotTradingFilters()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ConnectivityVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://www.okx.com/") };
        var catalog = new OkxSpotInstrumentCatalog(httpClient);

        var metadata = await catalog.GetAsync(
            InstrumentId.Create("OKX", "BTC-USDT"),
            CancellationToken.None);

        Assert.True(metadata.IsTradingEnabled);
        Assert.Equal("live", metadata.State);
        Assert.True(metadata.PriceTickSize > 0m);
        Assert.True(metadata.QuantityStepSize > 0m);
        Assert.True(metadata.MinimumQuantity > 0m);
    }

    [Fact]
    public async Task PublicHistoryReturnsContiguousClosedCandles()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(ConnectivityVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var timeframe = Timeframe.Create(TimeSpan.FromMinutes(1));
        var now = TimeProvider.System.GetUtcNow();
        var currentOpen = DateTimeOffset.FromUnixTimeMilliseconds(
            now.ToUnixTimeMilliseconds() / 60_000 * 60_000);
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://www.okx.com/") };
        var client = new OkxClosedCandleHistoryClient(httpClient, TimeProvider.System);

        var toExclusive = currentOpen.AddMinutes(-1);
        var candles = await client.GetAsync(
            InstrumentId.Create("OKX", "BTC-USDT"),
            timeframe,
            toExclusive.AddMinutes(-2),
            toExclusive,
            CancellationToken.None);

        Assert.Equal(2, candles.Count);
        Assert.Equal(toExclusive.AddMinutes(-2), candles[0].OpenTime);
        Assert.Equal(toExclusive.AddMinutes(-1), candles[1].OpenTime);
    }
}
