namespace TradingBot.Host;

public sealed class ForwardEvidenceOptions
{
    public const string SectionName = "ForwardEvidence";

    public bool Enabled { get; init; }

    public string PipelineId { get; init; } = "btc-usdt-v6-forward";

    public string RootPath { get; init; } = "data/forward-evidence";

    public DateTimeOffset StartInclusive { get; init; } =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    public int PollingIntervalSeconds { get; init; } = 60;

    public decimal MinimumNotional { get; init; } = 1m;
}
