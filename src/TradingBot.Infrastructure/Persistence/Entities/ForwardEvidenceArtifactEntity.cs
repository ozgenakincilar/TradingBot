namespace TradingBot.Infrastructure.Persistence.Entities;

public sealed class ForwardEvidenceArtifactEntity
{
    public required string WindowSha256 { get; set; }

    public required string PipelineId { get; set; }

    public int WindowIndex { get; set; }

    public DateTimeOffset StartInclusive { get; set; }

    public DateTimeOffset EndExclusive { get; set; }

    public required string ManifestPath { get; set; }

    public required string ManifestSha256 { get; set; }

    public required string SignalPath { get; set; }

    public required string SignalSourceId { get; set; }

    public required string SignalSha256 { get; set; }

    public long SignalCandleCount { get; set; }

    public long SignalTimeframeSeconds { get; set; }

    public required string TrendPath { get; set; }

    public required string TrendSourceId { get; set; }

    public required string TrendSha256 { get; set; }

    public long TrendCandleCount { get; set; }

    public long TrendTimeframeSeconds { get; set; }

    public DateTimeOffset SealedAt { get; set; }
}
