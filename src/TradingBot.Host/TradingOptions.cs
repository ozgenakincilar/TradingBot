using TradingBot.Domain;

namespace TradingBot.Host;

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public TradingMode Mode { get; init; } = TradingMode.Paper;

    public string Symbol { get; init; } = "BTCUSDT";

    public int PollingIntervalSeconds { get; init; } = 5;
}
