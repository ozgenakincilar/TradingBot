using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.Abstractions;

public sealed record PaperMarketEvent(
    string EventId,
    PaperTopOfBookSnapshot Snapshot);

public interface IMarketDataClient
{
    ValueTask<PaperMarketEvent> GetTopOfBookAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken);
}
