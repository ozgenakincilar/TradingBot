using TradingBot.Domain;

namespace TradingBot.Host;

public enum MarketDataSource
{
    Paper = 1,
    OkxPublic = 2
}

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public TradingMode Mode { get; init; } = TradingMode.Paper;

    public MarketDataSource MarketDataSource { get; init; } = MarketDataSource.Paper;

    public string Exchange { get; init; } = "PAPER";

    public string Symbol { get; init; } = "BTCUSDT";

    public int PollingIntervalSeconds { get; init; } = 5;

    public int MaximumMarketDataAgeSeconds { get; init; } = 15;

    public int MinimumFillLatencyMilliseconds { get; init; } = 100;

    public decimal CommissionPercent { get; init; } = 0.1m;

    public decimal SlippageBasisPoints { get; init; } = 10m;

    public decimal MaximumLiquidityParticipationPercent { get; init; } = 25m;

    public int CandleTimeframeSeconds { get; init; }

    public int WarmupCandleCount { get; init; }

    public string OkxRestBaseAddress { get; init; } = "https://www.okx.com/";

    public string OkxWebSocketEndpoint { get; init; } = "wss://ws.okx.com:8443/ws/v5/public";
}
