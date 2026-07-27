using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;

namespace TradingBot.Infrastructure.Backtesting;

public sealed class ImmutableForwardEvidenceArtifactStore(
    string rootPath,
    IClosedCandleHistoryClient historyClient,
    TimeProvider timeProvider) : IForwardEvidenceArtifactStore
{
    private const int BufferSize = 65_536;
    private const string ManifestFileName = "manifest.json";
    private readonly string _rootPath = ValidateRoot(rootPath);

    public async ValueTask<ForwardEvidenceArtifact> SealAsync(
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (window != policy.GetWindow(window.Index))
        {
            throw new DomainRuleViolationException(
                "Forward evidence store received an invalid window.");
        }

        Directory.CreateDirectory(_rootPath);
        var directoryName = $"window-{window.Index:D4}-{window.IdentitySha256[..12]}";
        var finalDirectory = Path.Combine(_rootPath, policy.PipelineId, directoryName);
        if (Directory.Exists(finalDirectory))
        {
            return await LoadExistingAsync(policy, window, finalDirectory, cancellationToken);
        }

        var pipelineDirectory = Path.Combine(_rootPath, policy.PipelineId);
        Directory.CreateDirectory(pipelineDirectory);
        var stagingDirectory = Path.Combine(
            pipelineDirectory,
            $".{directoryName}.partial-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var published = false;
        try
        {
            var sink = new AtomicCsvHistoricalCandleDatasetSink();
            var exporter = new ExportHistoricalCandleDataset(
                historyClient,
                sink,
                timeProvider);
            var signal = await ExportAsync(
                exporter,
                policy,
                window,
                policy.SignalTimeframe,
                Path.Combine(stagingDirectory, "signal-15m.csv"),
                "signal-15m",
                cancellationToken);
            var trend = await ExportAsync(
                exporter,
                policy,
                window,
                policy.TrendTimeframe,
                Path.Combine(stagingDirectory, "trend-1h.csv"),
                "trend-1h",
                cancellationToken);
            var sealedAt = timeProvider.GetUtcNow();
            if (sealedAt.Offset != TimeSpan.Zero || sealedAt < window.EndExclusive)
            {
                throw new DomainRuleViolationException(
                    "Forward evidence seal time must be UTC and cover the complete window.");
            }

            var manifest = new StoredManifest(
                ForwardEvidencePolicy.SchemaVersion,
                policy.PipelineId,
                policy.InstrumentId.ToString(),
                window.Index,
                window.IdentitySha256,
                window.StartInclusive,
                window.EndExclusive,
                sealedAt,
                ToEntry(signal, "signal-15m.csv"),
                ToEntry(trend, "trend-1h.csv"));
            var manifestPath = Path.Combine(stagingDirectory, ManifestFileName);
            await WriteManifestAsync(manifestPath, manifest, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(stagingDirectory, finalDirectory);
                published = true;
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                return await LoadExistingAsync(
                    policy,
                    window,
                    finalDirectory,
                    cancellationToken);
            }

            MakeReadOnly(finalDirectory);
            return await LoadExistingAsync(
                policy,
                window,
                finalDirectory,
                cancellationToken);
        }
        finally
        {
            if (!published && Directory.Exists(stagingDirectory))
            {
                EnsureChildDirectory(pipelineDirectory, stagingDirectory);
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static async ValueTask<HistoricalCandleExportArtifact> ExportAsync(
        ExportHistoricalCandleDataset exporter,
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow window,
        TradingBot.Domain.MarketData.Timeframe timeframe,
        string path,
        string role,
        CancellationToken cancellationToken)
    {
        var sourceId = $"okx-tr-{policy.PipelineId}-{window.Index:D4}-{role}";
        return await exporter.ExecuteAsync(
            new HistoricalCandleExportRequest(
                policy.InstrumentId,
                timeframe,
                window.StartInclusive,
                window.EndExclusive,
                sourceId,
                path),
            cancellationToken);
    }

    private static StoredDataset ToEntry(
        HistoricalCandleExportArtifact artifact,
        string fileName) =>
        new(
            fileName,
            artifact.Descriptor.SourceId,
            artifact.Descriptor.Sha256,
            artifact.Summary.CandleCount,
            (long)artifact.Descriptor.Timeframe.Duration.TotalSeconds);

    private static async ValueTask<ForwardEvidenceArtifact> LoadExistingAsync(
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow window,
        string directory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new DomainRuleViolationException(
                "Published forward evidence directory has no immutable manifest.");
        }

        StoredManifest manifest;
        await using (var stream = OpenRead(manifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync<StoredManifest>(
                stream,
                cancellationToken: cancellationToken)
                ?? throw new DomainRuleViolationException(
                    "Forward evidence manifest was empty.");
        }

        if (!string.Equals(manifest.SchemaVersion, ForwardEvidencePolicy.SchemaVersion,
                StringComparison.Ordinal) ||
            !string.Equals(manifest.PipelineId, policy.PipelineId, StringComparison.Ordinal) ||
            !string.Equals(manifest.InstrumentId, policy.InstrumentId.ToString(),
                StringComparison.Ordinal) ||
            manifest.WindowIndex != window.Index ||
            !string.Equals(manifest.WindowSha256, window.IdentitySha256,
                StringComparison.Ordinal) ||
            manifest.StartInclusive != window.StartInclusive ||
            manifest.EndExclusive != window.EndExclusive ||
            manifest.SealedAt < window.EndExclusive)
        {
            throw new DomainRuleViolationException(
                "Forward evidence manifest does not match its locked window.");
        }

        var signal = await ValidateDatasetAsync(
            directory,
            manifest.Signal,
            policy.SignalTimeframe,
            cancellationToken);
        var trend = await ValidateDatasetAsync(
            directory,
            manifest.Trend,
            policy.TrendTimeframe,
            cancellationToken);
        var manifestSha256 = await HashAsync(manifestPath, cancellationToken);
        return new ForwardEvidenceArtifact(
            policy.PipelineId,
            window,
            manifestPath,
            manifestSha256,
            signal,
            trend,
            manifest.SealedAt);
    }

    private static async ValueTask<ForwardEvidenceDatasetArtifact> ValidateDatasetAsync(
        string directory,
        StoredDataset dataset,
        TradingBot.Domain.MarketData.Timeframe expectedTimeframe,
        CancellationToken cancellationToken)
    {
        if (dataset.TimeframeSeconds != (long)expectedTimeframe.Duration.TotalSeconds ||
            string.IsNullOrWhiteSpace(dataset.FileName) ||
            Path.GetFileName(dataset.FileName) != dataset.FileName ||
            string.IsNullOrWhiteSpace(dataset.SourceId) ||
            dataset.CandleCount <= 0 ||
            dataset.Sha256.Length != 64)
        {
            throw new DomainRuleViolationException(
                "Forward evidence dataset manifest entry is invalid.");
        }

        var path = Path.Combine(directory, dataset.FileName);
        if (!File.Exists(path) ||
            !string.Equals(
                await HashAsync(path, cancellationToken),
                dataset.Sha256,
                StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "Forward evidence dataset failed SHA-256 verification.");
        }

        return new ForwardEvidenceDatasetArtifact(
            path,
            dataset.SourceId,
            dataset.Sha256,
            dataset.CandleCount,
            expectedTimeframe);
    }

    private static async Task WriteManifestAsync(
        string path,
        StoredManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, manifest, cancellationToken: cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void MakeReadOnly(string directory)
    {
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
    }

    private static string ValidateRoot(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new DomainRuleViolationException("Forward evidence root path is required.");
        }

        return Path.GetFullPath(rootPath);
    }

    private static void EnsureChildDirectory(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        var resolvedChild = Path.GetFullPath(child);
        if (!resolvedChild.StartsWith(resolvedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException(
                "Forward evidence temporary directory escaped its pipeline root.");
        }
    }

    private sealed record StoredManifest(
        string SchemaVersion,
        string PipelineId,
        string InstrumentId,
        int WindowIndex,
        string WindowSha256,
        DateTimeOffset StartInclusive,
        DateTimeOffset EndExclusive,
        DateTimeOffset SealedAt,
        StoredDataset Signal,
        StoredDataset Trend);

    private sealed record StoredDataset(
        string FileName,
        string SourceId,
        string Sha256,
        long CandleCount,
        long TimeframeSeconds);
}
