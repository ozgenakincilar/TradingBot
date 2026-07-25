namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class SpotPositionEntity
{
    public required string Exchange { get; set; }

    public required string Symbol { get; set; }

    public required string BaseAsset { get; set; }

    public required string QuoteAsset { get; set; }

    public decimal OpenQuantity { get; set; }

    public decimal ReservedSellQuantity { get; set; }

    public decimal AverageEntryPrice { get; set; }

    public decimal RealizedPnl { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
