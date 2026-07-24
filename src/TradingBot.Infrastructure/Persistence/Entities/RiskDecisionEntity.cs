namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class RiskDecisionEntity
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public byte DecisionType { get; set; }

    public decimal? ApprovedQuantity { get; set; }

    public int RejectionCode { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
