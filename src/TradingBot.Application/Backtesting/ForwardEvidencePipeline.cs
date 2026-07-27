using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public sealed record ForwardEvidencePolicy(
    string PipelineId,
    InstrumentId InstrumentId,
    Timeframe SignalTimeframe,
    Timeframe TrendTimeframe,
    DateTimeOffset StartInclusive)
{
    public const string SchemaVersion = "forward-evidence-v1";
    public const int MaximumWindowCount = 10_000;
    public static readonly TimeSpan WindowDuration = TimeSpan.FromDays(30);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PipelineId) || PipelineId.Length > 64 ||
            PipelineId.Any(static character =>
                character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')
                    and not '-' and not '_') ||
            InstrumentId == default || SignalTimeframe == default || TrendTimeframe == default ||
            SignalTimeframe.Duration != TimeSpan.FromMinutes(15) ||
            TrendTimeframe.Duration != TimeSpan.FromHours(1) ||
            StartInclusive.Offset != TimeSpan.Zero ||
            StartInclusive < AtrHysteresisValidationOrchestrator.EarliestForwardData ||
            !SignalTimeframe.IsBoundary(StartInclusive) ||
            !TrendTimeframe.IsBoundary(StartInclusive))
        {
            throw new DomainRuleViolationException("Forward evidence policy is invalid.");
        }
    }

    public int GetCompletedWindowCount(DateTimeOffset knownAt)
    {
        Validate();
        if (knownAt == default || knownAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Forward evidence knowledge time must be UTC.");
        }

        if (knownAt <= StartInclusive)
        {
            return 0;
        }

        var count = (knownAt - StartInclusive).Ticks / WindowDuration.Ticks;
        if (count > MaximumWindowCount)
        {
            throw new DomainRuleViolationException(
                "Forward evidence window count exceeds its bounded policy.");
        }

        return (int)count;
    }

    public ForwardEvidenceWindow GetWindow(int index)
    {
        Validate();
        if (index is < 0 or >= MaximumWindowCount)
        {
            throw new DomainRuleViolationException("Forward evidence window index is invalid.");
        }

        try
        {
            var start = StartInclusive.AddTicks(checked(WindowDuration.Ticks * index));
            var end = start.Add(WindowDuration);
            var identity = Hash(new
            {
                SchemaVersion,
                PipelineId,
                InstrumentId = InstrumentId.ToString(),
                SignalSeconds = (long)SignalTimeframe.Duration.TotalSeconds,
                TrendSeconds = (long)TrendTimeframe.Duration.TotalSeconds,
                Index = index,
                StartInclusive = start,
                EndExclusive = end
            });
            return new ForwardEvidenceWindow(index, start, end, identity);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "Forward evidence window exceeded the supported timestamp range.");
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Forward evidence window exceeded the supported timestamp range.");
        }
    }

    private static string Hash<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}

public readonly record struct ForwardEvidenceWindow(
    int Index,
    DateTimeOffset StartInclusive,
    DateTimeOffset EndExclusive,
    string IdentitySha256);

public sealed record ForwardEvidenceDatasetArtifact(
    string FilePath,
    string SourceId,
    string Sha256,
    long CandleCount,
    Timeframe Timeframe);

public sealed record ForwardEvidenceArtifact(
    string PipelineId,
    ForwardEvidenceWindow Window,
    string ManifestPath,
    string ManifestSha256,
    ForwardEvidenceDatasetArtifact Signal,
    ForwardEvidenceDatasetArtifact Trend,
    DateTimeOffset SealedAt);

public sealed record ForwardEvidenceEvaluation(
    string PipelineId,
    int SealedWindowCount,
    DateTimeOffset EvaluatedAt,
    string RunSha256,
    string ReportSha256,
    string ReportPath,
    string ReportFileSha256,
    AtrHysteresisValidationAcceptance Acceptance);

public interface IForwardEvidenceArtifactStore
{
    ValueTask<ForwardEvidenceArtifact> SealAsync(
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow window,
        CancellationToken cancellationToken);
}

public interface IForwardEvidenceEvaluator
{
    ValueTask<ForwardEvidenceEvaluation?> EvaluateAsync(
        ForwardEvidencePolicy policy,
        IReadOnlyList<ForwardEvidenceArtifact> artifacts,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken);
}

public readonly record struct ForwardEvidencePipelineResult(
    int CompletedWindowCount,
    int SealedWindowCount,
    bool WindowSealed,
    bool EvaluationStored,
    bool? IsAccepted);

public sealed class ForwardEvidencePipeline(
    IForwardEvidenceArtifactStore artifactStore,
    IForwardEvidenceEvaluator evaluator,
    IForwardEvidenceRepository repository,
    ITradingUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async ValueTask<ForwardEvidencePipelineResult> RunOnceAsync(
        ForwardEvidencePolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var knownAt = timeProvider.GetUtcNow();
        var completedCount = policy.GetCompletedWindowCount(knownAt);
        var artifacts = await repository.ListArtifactsAsync(
            policy.PipelineId,
            cancellationToken);
        ValidateArtifacts(policy, artifacts, completedCount);

        var sealedWindow = false;
        if (artifacts.Count < completedCount)
        {
            var window = policy.GetWindow(artifacts.Count);
            var artifact = await artifactStore.SealAsync(policy, window, cancellationToken);
            ValidateArtifact(policy, window, artifact);
            await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
            {
                var existing = await repository.GetArtifactAsync(
                    artifact.Window.IdentitySha256,
                    transactionCancellationToken);
                if (existing is null)
                {
                    repository.AddArtifact(artifact);
                }
                else if (!StringComparer.Ordinal.Equals(
                             existing.ManifestSha256,
                             artifact.ManifestSha256))
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence window identity produced a conflicting manifest.");
                }
            }, cancellationToken);
            sealedWindow = true;
            artifacts = await repository.ListArtifactsAsync(
                policy.PipelineId,
                cancellationToken);
            ValidateArtifacts(policy, artifacts, completedCount);
        }

        var latestEvaluation = await repository.GetLatestEvaluationAsync(
            policy.PipelineId,
            cancellationToken);
        var evaluation = latestEvaluation?.SealedWindowCount == artifacts.Count
            ? null
            : await evaluator.EvaluateAsync(
                policy,
                artifacts,
                knownAt,
                cancellationToken);
        var evaluationStored = false;
        if (evaluation is not null)
        {
            await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
            {
                var existing = await repository.GetEvaluationAsync(
                    evaluation.RunSha256,
                    transactionCancellationToken);
                if (existing is null)
                {
                    repository.AddEvaluation(evaluation);
                    evaluationStored = true;
                }
                else if (!StringComparer.Ordinal.Equals(
                             existing.ReportSha256,
                             evaluation.ReportSha256))
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence run identity produced a conflicting report.");
                }
            }, cancellationToken);
        }

        return new ForwardEvidencePipelineResult(
            completedCount,
            artifacts.Count,
            sealedWindow,
            evaluationStored,
            evaluation?.Acceptance.IsAccepted ?? latestEvaluation?.IsAccepted);
    }

    private static void ValidateArtifacts(
        ForwardEvidencePolicy policy,
        IReadOnlyList<ForwardEvidenceArtifact> artifacts,
        int completedCount)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count > completedCount)
        {
            throw new DomainRuleViolationException(
                "Forward evidence repository contains a future window.");
        }

        for (var index = 0; index < artifacts.Count; index++)
        {
            ValidateArtifact(policy, policy.GetWindow(index), artifacts[index]);
        }
    }

    private static void ValidateArtifact(
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow expected,
        ForwardEvidenceArtifact actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (!StringComparer.Ordinal.Equals(actual.PipelineId, policy.PipelineId) ||
            actual.Window != expected ||
            actual.Signal.Timeframe != policy.SignalTimeframe ||
            actual.Trend.Timeframe != policy.TrendTimeframe ||
            actual.Signal.CandleCount != 2_880 ||
            actual.Trend.CandleCount != 720 ||
            actual.SealedAt < expected.EndExclusive ||
            actual.SealedAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException(
                "Forward evidence artifact does not match its locked window.");
        }
    }
}
