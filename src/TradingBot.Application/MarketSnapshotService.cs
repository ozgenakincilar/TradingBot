using TradingBot.Application.Abstractions;
using TradingBot.Domain;

namespace TradingBot.Application;

public sealed class MarketSnapshotService(IMarketDataClient marketDataClient)
{
    public ValueTask<MarketPrice> GetAsync(string symbol, CancellationToken cancellationToken) =>
        marketDataClient.GetLatestPriceAsync(symbol, cancellationToken);
}
