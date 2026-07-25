namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class ExecutionOrderEntity
{
    public Guid Id { get; set; }

    public required string ClientOrderId { get; set; }

    public required string Exchange { get; set; }

    public required string Symbol { get; set; }

    public byte Side { get; set; }

    public byte Type { get; set; }

    public byte Status { get; set; }

    public decimal RequestedQuantity { get; set; }

    public decimal ApprovedQuantity { get; set; }

    public decimal? LimitPrice { get; set; }

    public decimal FilledQuantity { get; set; }

    public decimal? AverageFillPrice { get; set; }

    public string? ExchangeOrderId { get; set; }

    public string? RejectionReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
