using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Abstractions;

public interface IClosedCandleStreamClient
{
    IAsyncEnumerable<Candle> ReadClosedAsync(
        InstrumentId instrumentId,
        IReadOnlyCollection<Timeframe> timeframes,
        CancellationToken cancellationToken);
}
