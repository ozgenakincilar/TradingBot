namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class SpotOrderReservationEntity
{
    public Guid OrderId { get; set; }

    public required string Exchange { get; set; }

    public required string Symbol { get; set; }

    public required string BaseAsset { get; set; }

    public required string QuoteAsset { get; set; }

    public byte Side { get; set; }

    public decimal ApprovedQuantity { get; set; }

    public decimal FilledQuantity { get; set; }

    public decimal RemainingReserved { get; set; }

    public byte Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
