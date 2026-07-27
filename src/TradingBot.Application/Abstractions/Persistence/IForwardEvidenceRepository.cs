using TradingBot.Application.Backtesting;

namespace TradingBot.Application.Abstractions.Persistence;

public sealed record StoredForwardEvidenceArtifact(
    string WindowSha256,
    string ManifestSha256);

public sealed record StoredForwardEvidenceEvaluation(
    string PipelineId,
    int SealedWindowCount,
    string RunSha256,
    string ReportSha256,
    bool IsAccepted);

public interface IForwardEvidenceRepository
{
    Task<IReadOnlyList<ForwardEvidenceArtifact>> ListArtifactsAsync(
        string pipelineId,
        CancellationToken cancellationToken);

    Task<StoredForwardEvidenceArtifact?> GetArtifactAsync(
        string windowSha256,
        CancellationToken cancellationToken);

    Task<StoredForwardEvidenceEvaluation?> GetEvaluationAsync(
        string runSha256,
        CancellationToken cancellationToken);

    Task<StoredForwardEvidenceEvaluation?> GetLatestEvaluationAsync(
        string pipelineId,
        CancellationToken cancellationToken);

    void AddArtifact(ForwardEvidenceArtifact artifact);

    void AddEvaluation(ForwardEvidenceEvaluation evaluation);
}
