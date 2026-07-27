using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Backtesting;

internal sealed class ForwardEvidenceCsvDatasetFactory(
    ForwardEvidencePolicy policy,
    IReadOnlyList<ForwardEvidenceArtifact> artifacts,
    DateTimeOffset knownAt) : IHistoricalCandleDatasetFactory
{
    public ValueTask<IHistoricalCandleDataset> OpenAsync(
        InstrumentId instrumentId,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (instrumentId != policy.InstrumentId ||
            timeframe != policy.SignalTimeframe && timeframe != policy.TrendTimeframe ||
            artifacts.Count == 0 || knownAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException(
                "Forward evidence CSV dataset request is invalid.");
        }

        var partitions = new ForwardEvidenceDatasetArtifact[artifacts.Count];
        for (var index = 0; index < artifacts.Count; index++)
        {
            var artifact = artifacts[index];
            if (artifact.Window.Index != index)
            {
                throw new DomainRuleViolationException(
                    "Forward evidence CSV partitions must be ordered and contiguous.");
            }

            partitions[index] = timeframe == policy.SignalTimeframe
                ? artifact.Signal
                : artifact.Trend;
        }

        IHistoricalCandleDataset dataset = new PartitionedDataset(
            policy,
            timeframe,
            partitions,
            knownAt);
        return ValueTask.FromResult(dataset);
    }

    private sealed class PartitionedDataset : IHistoricalCandleDataset
    {
        private readonly InstrumentId _instrumentId;
        private readonly Timeframe _timeframe;
        private readonly ForwardEvidenceDatasetArtifact[] _partitions;
        private readonly DateTimeOffset _knownAt;
        private int _readStarted;

        public PartitionedDataset(
            ForwardEvidencePolicy policy,
            Timeframe timeframe,
            ForwardEvidenceDatasetArtifact[] partitions,
            DateTimeOffset knownAt)
        {
            _instrumentId = policy.InstrumentId;
            _timeframe = timeframe;
            _partitions = partitions;
            _knownAt = knownAt;
            var hash = CombinedHash(partitions);
            var role = timeframe == policy.SignalTimeframe ? "signal" : "trend";
            Descriptor = new HistoricalCandleDatasetDescriptor(
                $"{policy.PipelineId}-{role}-{partitions.Length:D4}",
                HistoricalCandleDatasetContract.CsvSchemaVersion,
                hash,
                policy.InstrumentId,
                timeframe);
            HistoricalCandleDatasetContract.ValidateDescriptor(Descriptor);
        }

        public HistoricalCandleDatasetDescriptor Descriptor { get; }

        public HistoricalCandleDatasetSummary? CompletedSummary { get; private set; }

        public IAsyncEnumerable<Candle> ReadAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _readStarted, 1) != 0)
            {
                throw new InvalidOperationException(
                    "Forward evidence dataset can only be streamed once.");
            }

            return ReadCoreAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async IAsyncEnumerable<Candle> ReadCoreAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            long count = 0;
            Candle? previous = null;
            DateTimeOffset firstOpen = default;
            for (var partitionIndex = 0;
                 partitionIndex < _partitions.Length;
                 partitionIndex++)
            {
                var partition = _partitions[partitionIndex];
                await using var dataset = await CsvHistoricalCandleDataset.OpenAsync(
                    partition.FilePath,
                    partition.SourceId,
                    _instrumentId,
                    _timeframe,
                    _knownAt,
                    cancellationToken);
                if (!string.Equals(
                        dataset.Descriptor.Sha256,
                        partition.Sha256,
                        StringComparison.Ordinal))
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence CSV partition hash changed after sealing.");
                }

                long partitionCount = 0;
                await foreach (var candle in dataset.ReadAsync(cancellationToken))
                {
                    if (previous is not null && candle.OpenTime != previous.CloseTime)
                    {
                        throw new DomainRuleViolationException(
                            "Forward evidence CSV partitions contain a gap.");
                    }

                    if (count == 0)
                    {
                        firstOpen = candle.OpenTime;
                    }

                    previous = candle;
                    count = checked(count + 1);
                    partitionCount = checked(partitionCount + 1);
                    yield return candle;
                }

                if (partitionCount != partition.CandleCount)
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence CSV partition count changed after sealing.");
                }
            }

            if (count == 0 || previous is null)
            {
                throw new DomainRuleViolationException(
                    "Forward evidence CSV dataset cannot be empty.");
            }

            CompletedSummary = new HistoricalCandleDatasetSummary(
                count,
                firstOpen,
                previous.CloseTime);
        }

        private static string CombinedHash(
            ForwardEvidenceDatasetArtifact[] partitions)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> bytes = stackalloc byte[64];
            for (var index = 0; index < partitions.Length; index++)
            {
                var sha = partitions[index].Sha256;
                if (sha.Length != 64)
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence partition SHA-256 is invalid.");
                }

                var written = Encoding.ASCII.GetBytes(sha.AsSpan(), bytes);
                if (written != bytes.Length)
                {
                    throw new DomainRuleViolationException(
                        "Forward evidence partition SHA-256 is invalid.");
                }

                hash.AppendData(bytes);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }
}
