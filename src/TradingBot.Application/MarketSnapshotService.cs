using TradingBot.Application.Abstractions;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application;

public sealed class MarketSnapshotService(IMarketDataClient marketDataClient)
{
    public ValueTask<PaperMarketEvent> GetAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken) =>
        marketDataClient.GetTopOfBookAsync(instrumentId, cancellationToken);
}
