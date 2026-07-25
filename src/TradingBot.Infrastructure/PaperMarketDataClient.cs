using TradingBot.Application.Abstractions;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure;

public sealed class PaperMarketDataClient(TimeProvider timeProvider) : IMarketDataClient
{
    public ValueTask<PaperMarketEvent> GetTopOfBookAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Deterministic placeholder until an exchange adapter is selected.
        var occurredAt = timeProvider.GetUtcNow();
        return ValueTask.FromResult(
            new PaperMarketEvent(
                $"paper-{instrumentId.Exchange}-{instrumentId.Symbol}-{occurredAt.UtcTicks}",
                new PaperTopOfBookSnapshot(
                    instrumentId,
                    Price.From(99_999m),
                    1m,
                    Price.From(100_001m),
                    1m,
                    occurredAt)));
    }
}
