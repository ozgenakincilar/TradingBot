using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class ForwardEvidenceRepository(TradingBotDbContext context) :
    IForwardEvidenceRepository
{
    public async Task<IReadOnlyList<ForwardEvidenceArtifact>> ListArtifactsAsync(
        string pipelineId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        var entities = await context.ForwardEvidenceArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.PipelineId == pipelineId)
            .OrderBy(static artifact => artifact.WindowIndex)
            .ToListAsync(cancellationToken);
        var artifacts = new ForwardEvidenceArtifact[entities.Count];
        for (var index = 0; index < entities.Count; index++)
        {
            artifacts[index] = Map(entities[index]);
        }

        return Array.AsReadOnly(artifacts);
    }

    public Task<StoredForwardEvidenceArtifact?> GetArtifactAsync(
        string windowSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowSha256);
        return context.ForwardEvidenceArtifacts
            .AsNoTracking()
            .Where(artifact => artifact.WindowSha256 == windowSha256)
            .Select(static artifact => new StoredForwardEvidenceArtifact(
                artifact.WindowSha256,
                artifact.ManifestSha256))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<StoredForwardEvidenceEvaluation?> GetEvaluationAsync(
        string runSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runSha256);
        return context.ForwardEvidenceEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.RunSha256 == runSha256)
            .Select(static evaluation => new StoredForwardEvidenceEvaluation(
                evaluation.PipelineId,
                evaluation.SealedWindowCount,
                evaluation.RunSha256,
                evaluation.ReportSha256,
                evaluation.IsAccepted))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<StoredForwardEvidenceEvaluation?> GetLatestEvaluationAsync(
        string pipelineId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineId);
        return context.ForwardEvidenceEvaluations
            .AsNoTracking()
            .Where(evaluation => evaluation.PipelineId == pipelineId)
            .OrderByDescending(static evaluation => evaluation.SealedWindowCount)
            .Select(static evaluation => new StoredForwardEvidenceEvaluation(
                evaluation.PipelineId,
                evaluation.SealedWindowCount,
                evaluation.RunSha256,
                evaluation.ReportSha256,
                evaluation.IsAccepted))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void AddArtifact(ForwardEvidenceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        context.ForwardEvidenceArtifacts.Add(new ForwardEvidenceArtifactEntity
        {
            WindowSha256 = artifact.Window.IdentitySha256,
            PipelineId = artifact.PipelineId,
            WindowIndex = artifact.Window.Index,
            StartInclusive = artifact.Window.StartInclusive,
            EndExclusive = artifact.Window.EndExclusive,
            ManifestPath = artifact.ManifestPath,
            ManifestSha256 = artifact.ManifestSha256,
            SignalPath = artifact.Signal.FilePath,
            SignalSourceId = artifact.Signal.SourceId,
            SignalSha256 = artifact.Signal.Sha256,
            SignalCandleCount = artifact.Signal.CandleCount,
            SignalTimeframeSeconds = (long)artifact.Signal.Timeframe.Duration.TotalSeconds,
            TrendPath = artifact.Trend.FilePath,
            TrendSourceId = artifact.Trend.SourceId,
            TrendSha256 = artifact.Trend.Sha256,
            TrendCandleCount = artifact.Trend.CandleCount,
            TrendTimeframeSeconds = (long)artifact.Trend.Timeframe.Duration.TotalSeconds,
            SealedAt = artifact.SealedAt
        });
    }

    public void AddEvaluation(ForwardEvidenceEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        var acceptance = evaluation.Acceptance;
        context.ForwardEvidenceEvaluations.Add(new ForwardEvidenceEvaluationEntity
        {
            RunSha256 = evaluation.RunSha256,
            PipelineId = evaluation.PipelineId,
            SealedWindowCount = evaluation.SealedWindowCount,
            ReportSha256 = evaluation.ReportSha256,
            ReportPath = evaluation.ReportPath,
            ReportFileSha256 = evaluation.ReportFileSha256,
            EvaluatedAt = evaluation.EvaluatedAt,
            MinimumTradesPassed = acceptance.MinimumTradesPassed,
            ProfitFactorPassed = acceptance.ProfitFactorPassed,
            PositiveNetReturnPassed = acceptance.PositiveNetReturnPassed,
            BenchmarkExcessPassed = acceptance.BenchmarkExcessPassed,
            DrawdownPassed = acceptance.DrawdownPassed,
            ProfitableWindowsPassed = acceptance.ProfitableWindowsPassed,
            ExecutionCostCoveragePassed = acceptance.ExecutionCostCoveragePassed,
            FullyExecutedPassed = acceptance.FullyExecutedPassed,
            IsAccepted = acceptance.IsAccepted
        });
    }

    private static ForwardEvidenceArtifact Map(ForwardEvidenceArtifactEntity entity)
    {
        var signalTimeframe = Timeframe.Create(TimeSpan.FromSeconds(
            entity.SignalTimeframeSeconds));
        var trendTimeframe = Timeframe.Create(TimeSpan.FromSeconds(
            entity.TrendTimeframeSeconds));
        return new ForwardEvidenceArtifact(
            entity.PipelineId,
            new ForwardEvidenceWindow(
                entity.WindowIndex,
                entity.StartInclusive,
                entity.EndExclusive,
                entity.WindowSha256),
            entity.ManifestPath,
            entity.ManifestSha256,
            new ForwardEvidenceDatasetArtifact(
                entity.SignalPath,
                entity.SignalSourceId,
                entity.SignalSha256,
                entity.SignalCandleCount,
                signalTimeframe),
            new ForwardEvidenceDatasetArtifact(
                entity.TrendPath,
                entity.TrendSourceId,
                entity.TrendSha256,
                entity.TrendCandleCount,
                trendTimeframe),
            entity.SealedAt);
    }
}
