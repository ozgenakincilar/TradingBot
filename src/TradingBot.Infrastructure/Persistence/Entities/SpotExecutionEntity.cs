namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class SpotExecutionEntity
{
    public required string Exchange { get; set; }

    public required string ExchangeExecutionId { get; set; }

    public required string Symbol { get; set; }

    public byte Side { get; set; }

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal QuoteFee { get; set; }

    public decimal RealizedPnl { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public required string CorrelationId { get; set; }
}
