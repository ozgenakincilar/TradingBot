using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Orders;

namespace TradingBot.Application.Execution;

public sealed record ProcessPaperMarketEventCommand(
    PaperMarketEvent MarketEvent,
    PaperExecutionPolicy Policy,
    string CorrelationId);

public sealed record PaperOrderEventOutcome(
    OrderId OrderId,
    ProcessPaperOrderSnapshotOutcome Outcome);

public sealed record ProcessPaperMarketEventOutcome(
    IReadOnlyCollection<PaperOrderEventOutcome> Orders);

public sealed class ProcessPaperMarketEvent(
    IPaperOrderReader paperOrders,
    ProcessPaperOrderSnapshot processOrder)
{
    public async Task<ProcessPaperMarketEventOutcome> HandleAsync(
        ProcessPaperMarketEventCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.MarketEvent);
        ArgumentNullException.ThrowIfNull(command.Policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.MarketEvent.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        if (command.MarketEvent.EventId.Length > 128 || command.CorrelationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        var orderIds = await paperOrders.GetActiveOrderIdsAsync(
            command.MarketEvent.Snapshot.InstrumentId,
            cancellationToken);
        var outcomes = new List<PaperOrderEventOutcome>(orderIds.Count);
        foreach (var orderId in orderIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await processOrder.HandleAsync(
                new ProcessPaperOrderSnapshotCommand(
                    orderId,
                    command.MarketEvent.EventId,
                    command.MarketEvent.Snapshot,
                    command.Policy,
                    command.CorrelationId),
                cancellationToken);
            outcomes.Add(new PaperOrderEventOutcome(orderId, outcome));
        }

        return new ProcessPaperMarketEventOutcome(outcomes);
    }
}
