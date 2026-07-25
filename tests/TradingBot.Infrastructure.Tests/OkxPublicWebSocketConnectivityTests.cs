using TradingBot.Domain.Instruments;
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
}
