using TradingBot.Application.Abstractions;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure;

public sealed class PaperMarketDataClient(TimeProvider timeProvider) : IMarketDataClient
{
    private long _sequence;

    public ValueTask<PaperMarketEvent> GetTopOfBookAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CreateEvent(
            instrumentId,
            Interlocked.Increment(ref _sequence),
            "stream"));
    }

    public ValueTask<PaperMarketEvent> GetRecoverySnapshotAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sequence = Interlocked.Read(ref _sequence);
        if (sequence == 0)
        {
            sequence = Interlocked.Increment(ref _sequence);
        }

        return ValueTask.FromResult(CreateEvent(instrumentId, sequence, "snapshot"));
    }

    private PaperMarketEvent CreateEvent(
        InstrumentId instrumentId,
        long sequence,
        string source)
    {
        // Deterministic placeholder until an exchange adapter is selected.
        var occurredAt = timeProvider.GetUtcNow();
        return new PaperMarketEvent(
            $"paper-{source}-{instrumentId.Exchange}-{instrumentId.Symbol}-{sequence}",
            sequence,
            occurredAt,
            new PaperTopOfBookSnapshot(
                instrumentId,
                Price.From(99_999m),
                1m,
                Price.From(100_001m),
                1m,
                occurredAt));
    }
}
