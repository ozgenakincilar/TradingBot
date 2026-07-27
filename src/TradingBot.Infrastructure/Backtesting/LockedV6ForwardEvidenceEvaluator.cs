using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure.Backtesting;

public sealed class LockedV6ForwardEvidenceEvaluator(
    ISpotInstrumentCatalog instrumentCatalog,
    string evidenceRootPath,
    decimal minimumNotional) : IForwardEvidenceEvaluator
{
    private const int BufferSize = 65_536;
    private const int MinimumDatasetPartitionCount =
        AtrHysteresisValidationOrchestrator.MinimumForwardWindowCount + 2;
    private readonly string _evidenceRootPath = ValidateRoot(evidenceRootPath);
    private readonly decimal _minimumNotional = minimumNotional > 0m
        ? minimumNotional
        : throw new DomainRuleViolationException(
            "Forward evidence minimum notional must be positive.");

    public async ValueTask<ForwardEvidenceEvaluation?> EvaluateAsync(
        ForwardEvidencePolicy policy,
        IReadOnlyList<ForwardEvidenceArtifact> artifacts,
        DateTimeOffset evaluatedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(artifacts);
        policy.Validate();
        if (artifacts.Count < MinimumDatasetPartitionCount)
        {
            return null;
        }

        if (evaluatedAt.Offset != TimeSpan.Zero ||
            evaluatedAt < artifacts[^1].Window.EndExclusive)
        {
            throw new DomainRuleViolationException(
                "Forward evidence evaluation time is invalid.");
        }

        ValidateSequence(policy, artifacts);
        var metadata = await instrumentCatalog.GetAsync(
            policy.InstrumentId,
            cancellationToken);
        if (!metadata.IsTradingEnabled || metadata.InstrumentId != policy.InstrumentId)
        {
            throw new DomainRuleViolationException(
                "Forward evidence instrument is not currently tradable.");
        }

        var instrument = Instrument.Create(
            metadata.InstrumentId,
            metadata.PriceTickSize,
            metadata.QuantityStepSize,
            metadata.MinimumQuantity,
            _minimumNotional);
        var locked = LockedAtrHysteresisV6Configuration.Create(instrument);
        var schedule = WalkForwardSchedule.Create(
            policy.StartInclusive,
            artifacts[^1].Window.EndExclusive,
            ForwardEvidencePolicy.WindowDuration,
            ForwardEvidencePolicy.WindowDuration,
            ForwardEvidencePolicy.WindowDuration,
            WalkForwardTrainingMode.Expanding,
            policy.SignalTimeframe,
            policy.TrendTimeframe);
        var datasets = new ForwardEvidenceCsvDatasetFactory(
            policy,
            artifacts,
            evaluatedAt);
        var orchestrator = new AtrHysteresisValidationOrchestrator(
            datasets,
            new DeterministicStrategyBacktest(),
            new BacktestExecutionSimulator(),
            new BuyAndHoldBenchmark());
        var report = await orchestrator.RunAsync(
            locked.Baseline,
            locked.Candidate,
            locked.ExecutionPolicy,
            schedule,
            locked.ParameterGrid,
            LockedAtrHysteresisV6Configuration.RandomSeed,
            cancellationToken);
        var reportArtifact = await WriteReportAsync(
            policy,
            artifacts.Count,
            report,
            cancellationToken);
        return new ForwardEvidenceEvaluation(
            policy.PipelineId,
            artifacts.Count,
            evaluatedAt,
            report.RunSha256,
            report.ReportSha256,
            reportArtifact.Path,
            reportArtifact.Sha256,
            report.Acceptance);
    }

    private async ValueTask<ReportArtifact> WriteReportAsync(
        ForwardEvidencePolicy policy,
        int artifactCount,
        AtrHysteresisValidationReport report,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_evidenceRootPath, policy.PipelineId, "evaluations");
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(
            directory,
            $"evaluation-{artifactCount:D4}-{report.RunSha256[..12]}.json");
        var temporaryPath = $"{targetPath}.partial-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    report,
                    cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            var candidateHash = await HashAsync(temporaryPath, cancellationToken);
            if (File.Exists(targetPath))
            {
                var existingHash = await HashAsync(targetPath, cancellationToken);
                if (!string.Equals(candidateHash, existingHash, StringComparison.Ordinal))
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence report identity produced conflicting bytes.");
                }

                File.Delete(temporaryPath);
                return new ReportArtifact(targetPath, existingHash);
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
            File.SetAttributes(
                targetPath,
                File.GetAttributes(targetPath) | FileAttributes.ReadOnly);
            return new ReportArtifact(targetPath, candidateHash);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateSequence(
        ForwardEvidencePolicy policy,
        IReadOnlyList<ForwardEvidenceArtifact> artifacts)
    {
        for (var index = 0; index < artifacts.Count; index++)
        {
            var artifact = artifacts[index];
            if (artifact.Window != policy.GetWindow(index) ||
                artifact.Signal.Timeframe != policy.SignalTimeframe ||
                artifact.Trend.Timeframe != policy.TrendTimeframe)
            {
                throw new DomainRuleViolationException(
                    "Forward evidence evaluation artifacts are not contiguous.");
            }
        }
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DomainRuleViolationException(
                "Forward evidence report root path is required.");
        }

        return Path.GetFullPath(path);
    }

    private readonly record struct ReportArtifact(string Path, string Sha256);
}
