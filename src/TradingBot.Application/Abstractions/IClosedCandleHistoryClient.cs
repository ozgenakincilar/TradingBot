using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Abstractions;

public interface IClosedCandleHistoryClient
{
    ValueTask<IReadOnlyList<Candle>> GetAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken);
}
