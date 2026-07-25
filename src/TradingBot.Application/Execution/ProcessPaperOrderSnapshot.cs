using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Execution;

public sealed record ProcessPaperOrderSnapshotCommand(
    OrderId OrderId,
    string MarketEventId,
    PaperTopOfBookSnapshot Market,
    PaperExecutionPolicy Policy,
    string CorrelationId);

public enum PaperOrderProcessingStatus
{
    WaitingForLatency = 1,
    WaitingForLimitPrice = 2,
    WaitingForLiquidity = 3,
    FillApplied = 4,
    FillAlreadyApplied = 5,
    OrderClosed = 6
}

public sealed record ProcessPaperOrderSnapshotOutcome(
    PaperOrderProcessingStatus Status,
    string? ExchangeExecutionId,
    decimal? FillQuantity,
    decimal? FillPrice,
    decimal? QuoteFee);

public sealed class ProcessPaperOrderSnapshot(
    IPaperOrderReader paperOrders,
    ApplySpotOrderFill applyFill)
{
    private readonly PaperExecutionEngine _engine = new();

    public async Task<ProcessPaperOrderSnapshotOutcome> HandleAsync(
        ProcessPaperOrderSnapshotCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var order = await paperOrders.GetAsync(command.OrderId, cancellationToken)
            ?? throw new DomainRuleViolationException("Paper order was not found.");
        if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected)
        {
            return new ProcessPaperOrderSnapshotOutcome(
                PaperOrderProcessingStatus.OrderClosed,
                null,
                null,
                null,
                null);
        }

        if (order.Status is not (OrderStatus.Open or OrderStatus.PartiallyFilled or OrderStatus.CancelPending))
        {
            throw new DomainRuleViolationException("Paper execution requires an active exchange order state.");
        }

        if (order.ReservationStatus != SpotReservationStatus.Active)
        {
            throw new DomainRuleViolationException("Paper order reservation is not active or consistent.");
        }

        var remaining = order.ApprovedQuantity - order.FilledQuantity;
        if (remaining <= 0m)
        {
            throw new DomainRuleViolationException("Active paper order has no remaining quantity.");
        }

        var execution = _engine.Evaluate(
            command.Policy,
            new PaperExecutionRequest(
                order.OrderId,
                order.InstrumentId,
                order.QuoteAsset,
                order.Side,
                order.Type,
                Quantity.From(remaining),
                order.LimitPrice,
                order.ReservationCreatedAt),
            command.Market);
        if (execution.Fill is null)
        {
            return new ProcessPaperOrderSnapshotOutcome(
                MapWaiting(execution.Status),
                null,
                null,
                null,
                null);
        }

        var executionId = CreateExecutionId(order.OrderId, command.MarketEventId);
        var persistenceResult = await applyFill.HandleAsync(
            new ApplySpotOrderFillCommand(
                order.OrderId,
                executionId,
                execution.Fill.Quantity,
                execution.Fill.Price,
                execution.Fill.QuoteFee,
                execution.Fill.OccurredAt,
                command.CorrelationId),
            cancellationToken);

        return new ProcessPaperOrderSnapshotOutcome(
            persistenceResult == SpotReservationOperationResult.AlreadyApplied
                ? PaperOrderProcessingStatus.FillAlreadyApplied
                : PaperOrderProcessingStatus.FillApplied,
            executionId,
            execution.Fill.Quantity.Value,
            execution.Fill.Price.Value,
            execution.Fill.QuoteFee.Amount);
    }

    private static string CreateExecutionId(OrderId orderId, string marketEventId)
    {
        var eventHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(marketEventId)))[..16];
        return $"PAPER-{orderId.Value:N}-{eventHash}";
    }

    private static PaperOrderProcessingStatus MapWaiting(PaperExecutionStatus status) =>
        status switch
        {
            PaperExecutionStatus.WaitingForLatency => PaperOrderProcessingStatus.WaitingForLatency,
            PaperExecutionStatus.WaitingForLimitPrice => PaperOrderProcessingStatus.WaitingForLimitPrice,
            PaperExecutionStatus.WaitingForLiquidity => PaperOrderProcessingStatus.WaitingForLiquidity,
            _ => throw new InvalidOperationException("Unexpected paper execution result.")
        };

    private static void Validate(ProcessPaperOrderSnapshotCommand command)
    {
        if (command.OrderId == default)
        {
            throw new ArgumentException("Order id is required.", nameof(command));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(command.MarketEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);
        ArgumentNullException.ThrowIfNull(command.Market);
        ArgumentNullException.ThrowIfNull(command.Policy);
        if (command.MarketEventId.Length > 128 || command.CorrelationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
