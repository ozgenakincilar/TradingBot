using TradingBot.Domain;

namespace TradingBot.Application.Abstractions;

public interface IMarketDataClient
{
    ValueTask<MarketPrice> GetLatestPriceAsync(
        string symbol,
        CancellationToken cancellationToken);
}
