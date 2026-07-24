using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;

namespace TradingBot.Application.Orders;

public sealed record PersistRiskApprovedOrderCommand(
    Order Order,
    RiskDecision RiskDecision,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public enum PersistOrderResult
{
    Stored = 1,
    AlreadyExists = 2
}

public sealed class PersistRiskApprovedOrder(
    IOrderRepository orders,
    IRiskDecisionRepository riskDecisions,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private const string IntegrationEventType = "order.risk-approved.v1";

    public async Task<PersistOrderResult> HandleAsync(
        PersistRiskApprovedOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var result = PersistOrderResult.Stored;
        await unitOfWork.ExecuteAsync(
            async transactionCancellationToken =>
            {
                if (await orders.ExistsAsync(
                        command.Order.ClientOrderId,
                        transactionCancellationToken))
                {
                    result = PersistOrderResult.AlreadyExists;
                    return;
                }

                var payload = SerializeEvent(command);
                orders.Add(command.Order);
                riskDecisions.Add(
                    idGenerator.NewGuid(),
                    command.Order.Id,
                    command.RiskDecision,
                    command.OccurredAt);
                audit.Add(CreateAuditRecord(command, payload));
                outbox.Add(CreateOutboxRecord(command, payload));
            },
            cancellationToken);

        return result;
    }

    private AuditRecord CreateAuditRecord(
        PersistRiskApprovedOrderCommand command,
        string payload) =>
        new(
            idGenerator.NewGuid(),
            command.OccurredAt,
            "Execution",
            "OrderRiskApproved",
            nameof(Order),
            command.Order.Id.Value.ToString("D"),
            command.CorrelationId,
            payload);

    private OutboxRecord CreateOutboxRecord(
        PersistRiskApprovedOrderCommand command,
        string payload) =>
        new(
            idGenerator.NewGuid(),
            command.OccurredAt,
            IntegrationEventType,
            command.CorrelationId,
            payload);

    private static string SerializeEvent(PersistRiskApprovedOrderCommand command) =>
        JsonSerializer.Serialize(new OrderRiskApprovedIntegrationEvent(
            SchemaVersion: 1,
            command.Order.Id.Value,
            command.Order.ClientOrderId.Value,
            command.Order.InstrumentId.Exchange,
            command.Order.InstrumentId.Symbol,
            command.Order.ApprovedQuantity.Value,
            command.RiskDecision.Type.ToString(),
            command.OccurredAt));

    private static void Validate(PersistRiskApprovedOrderCommand command)
    {
        ArgumentNullException.ThrowIfNull(command.Order);
        ArgumentNullException.ThrowIfNull(command.RiskDecision);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);

        if (command.CorrelationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Correlation id cannot exceed 64 characters.");
        }

        if (command.Order.Status != OrderStatus.RiskApproved)
        {
            throw new DomainRuleViolationException(
                "Only a risk-approved order can enter atomic persistence.");
        }

        if (command.RiskDecision.Type is not (RiskDecisionType.Approved or RiskDecisionType.Resized) ||
            command.RiskDecision.ApprovedQuantity is null)
        {
            throw new DomainRuleViolationException(
                "A rejected risk decision cannot create an executable order.");
        }

        if (command.RiskDecision.ApprovedQuantity.Value.Value !=
            command.Order.ApprovedQuantity.Value)
        {
            throw new DomainRuleViolationException(
                "Risk decision quantity must match the order approved quantity.");
        }

        if (command.OccurredAt < command.Order.UpdatedAt)
        {
            throw new DomainRuleViolationException(
                "Persistence event cannot occur before the latest order event.");
        }
    }

    private sealed record OrderRiskApprovedIntegrationEvent(
        int SchemaVersion,
        Guid OrderId,
        string ClientOrderId,
        string Exchange,
        string Symbol,
        decimal ApprovedQuantity,
        string RiskDecision,
        DateTimeOffset OccurredAt);
}
