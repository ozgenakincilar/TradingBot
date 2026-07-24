using TradingBot.Application.Abstractions;
using TradingBot.Domain;

namespace TradingBot.Infrastructure;

public sealed class PaperMarketDataClient(TimeProvider timeProvider) : IMarketDataClient
{
    public ValueTask<MarketPrice> GetLatestPriceAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Deterministic placeholder until an exchange adapter is selected.
        return ValueTask.FromResult(
            MarketPrice.Create(symbol, 100_000m, timeProvider.GetUtcNow()));
    }
}
