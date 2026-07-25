namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class TradingSafetyStateEntity
{
    public required string Exchange { get; set; }

    public bool IsHalted { get; set; }

    public string? HaltReason { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
