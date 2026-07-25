namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class AssetBalanceEntity
{
    public required string Exchange { get; set; }

    public required string Asset { get; set; }

    public decimal Total { get; set; }

    public decimal Reserved { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
