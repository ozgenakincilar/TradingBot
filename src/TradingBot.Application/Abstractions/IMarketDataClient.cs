using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.Abstractions;

public sealed record PaperMarketEvent(
    string EventId,
    long Sequence,
    DateTimeOffset ReceivedAt,
    PaperTopOfBookSnapshot Snapshot);

public interface IMarketDataClient
{
    ValueTask<PaperMarketEvent> GetTopOfBookAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken);

    ValueTask<PaperMarketEvent> GetRecoverySnapshotAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken);
}
