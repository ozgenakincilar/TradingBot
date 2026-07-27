using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Infrastructure.Tests;

public sealed class ForwardEvidenceSevenWindowEndToEndTests : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument =
        InstrumentId.Create("OKX", "BTC-USDT");
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tradingbot-forward-e2e-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstSixWindowsStaySilentAndSeventhProducesDeterministicReportHash()
    {
        var policy = Policy();
        var clock = new MutableClock(Start);
        var repository = new Repository();
        var store = new ImmutableForwardEvidenceArtifactStore(
            _root,
            new SyntheticHistory(clock),
            clock);
        var evaluator = new LockedV6ForwardEvidenceEvaluator(
            new InstrumentCatalog(),
            _root,
            minimumNotional: 1m);
        var pipeline = new ForwardEvidencePipeline(
            store,
            evaluator,
            repository,
            new UnitOfWork(),
            clock);

        for (var windowCount = 1; windowCount <= 6; windowCount++)
        {
            clock.Set(Start.AddDays(30 * windowCount));
            var result = await pipeline.RunOnceAsync(policy, CancellationToken.None);

            Assert.True(result.WindowSealed);
            Assert.False(result.EvaluationStored);
            Assert.Null(result.IsAccepted);
            Assert.Equal(windowCount, repository.Artifacts.Count);
            Assert.Empty(repository.Evaluations);
        }

        clock.Set(Start.AddDays(210));
        var seventh = await pipeline.RunOnceAsync(policy, CancellationToken.None);

        Assert.True(seventh.WindowSealed);
        Assert.True(seventh.EvaluationStored);
        var stored = Assert.Single(repository.Evaluations);
        var replay = await evaluator.EvaluateAsync(
            policy,
            repository.Artifacts,
            clock.GetUtcNow(),
            CancellationToken.None);
        Assert.NotNull(replay);
        Assert.Equal(stored.RunSha256, replay.RunSha256);
        Assert.Equal(stored.ReportSha256, replay.ReportSha256);
        Assert.Equal(stored.ReportFileSha256, replay.ReportFileSha256);
    }

    [Fact]
    public async Task MissingSingleCandleInSeventhWindowFailsClosedWithoutPublishing()
    {
        var policy = Policy();
        var clock = new MutableClock(Start.AddDays(210));
        var missingOpenTime = policy.GetWindow(6).StartInclusive.AddHours(12);
        var repository = new Repository();
        for (var index = 0; index < 6; index++)
        {
            repository.Artifacts.Add(PlaceholderArtifact(policy, index));
        }

        var pipeline = new ForwardEvidencePipeline(
            new ImmutableForwardEvidenceArtifactStore(
                _root,
                new SyntheticHistory(clock, missingOpenTime),
                clock),
            new RejectingEvaluator(),
            repository,
            new UnitOfWork(),
            clock);

        await Assert.ThrowsAsync<DomainRuleViolationException>(async () =>
            await pipeline.RunOnceAsync(policy, CancellationToken.None));

        Assert.Equal(6, repository.Artifacts.Count);
        Assert.Empty(repository.Evaluations);
        Assert.False(Directory.EnumerateFiles(
            _root,
            "manifest.json",
            SearchOption.AllDirectories).Any());
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }

    private static ForwardEvidencePolicy Policy() => new(
        "btc-usdt-v6-forward-e2e",
        Instrument,
        Timeframe.Create(TimeSpan.FromMinutes(15)),
        Timeframe.Create(TimeSpan.FromHours(1)),
        Start);

    private static ForwardEvidenceArtifact PlaceholderArtifact(
        ForwardEvidencePolicy policy,
        int index)
    {
        var window = policy.GetWindow(index);
        return new ForwardEvidenceArtifact(
            policy.PipelineId,
            window,
            $"placeholder-{index}/manifest.json",
            Hash($"manifest-{index}"),
            new ForwardEvidenceDatasetArtifact(
                $"placeholder-{index}/15m.csv",
                $"signal-{index}",
                Hash($"signal-{index}"),
                2_880,
                policy.SignalTimeframe),
            new ForwardEvidenceDatasetArtifact(
                $"placeholder-{index}/1h.csv",
                $"trend-{index}",
                Hash($"trend-{index}"),
                720,
                policy.TrendTimeframe),
            window.EndExclusive);
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Set(DateTimeOffset value) => _now = value;
    }

    private sealed class SyntheticHistory(
        TimeProvider clock,
        DateTimeOffset? missingOpenTime = null) : IClosedCandleHistoryClient
    {
        private static readonly Timeframe SignalTimeframe =
            Timeframe.Create(TimeSpan.FromMinutes(15));
        private static readonly decimal[] SignalWave =
            [-1.2m, -0.8m, -0.3m, 0.2m, 0.8m, 1.3m, 0.7m, 0.1m,
             -0.5m, -1.1m, -0.6m, 0.1m, 0.9m, 1.4m, 0.6m, -0.2m];

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedCount = (int)((toExclusive - fromInclusive).Ticks /
                timeframe.Duration.Ticks);
            var dropsCandle = missingOpenTime is { } missing &&
                              missing >= fromInclusive && missing < toExclusive &&
                              timeframe == SignalTimeframe;
            var candles = new Candle[expectedCount - (dropsCandle ? 1 : 0)];
            var target = 0;
            for (var index = 0; index < expectedCount; index++)
            {
                var openTime = fromInclusive + (timeframe.Duration * index);
                if (dropsCandle && openTime == missingOpenTime)
                {
                    continue;
                }

                var globalIndex = (openTime - Start).Ticks / timeframe.Duration.Ticks;
                decimal close;
                decimal range;
                if (timeframe.Duration == TimeSpan.FromMinutes(15))
                {
                    close = 100m + globalIndex / 10_000m +
                            SignalWave[(int)(globalIndex % SignalWave.Length)];
                    range = 0.45m;
                }
                else
                {
                    close = 90m + globalIndex / 100m;
                    range = 0.40m;
                }

                candles[target++] = Candle.CreateClosed(
                    instrumentId,
                    timeframe,
                    openTime,
                    clock.GetUtcNow(),
                    close,
                    close + range,
                    close - range,
                    close,
                    100_000m);
            }

            return ValueTask.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }

    private sealed class InstrumentCatalog : ISpotInstrumentCatalog
    {
        public ValueTask<SpotInstrumentMetadata> GetAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new SpotInstrumentMetadata(
                instrumentId,
                AssetCode.Create("BTC"),
                AssetCode.Create("USDT"),
                PriceTickSize: 0.01m,
                QuantityStepSize: 0.0001m,
                MinimumQuantity: 0.0001m,
                IsTradingEnabled: true,
                State: "live"));
        }
    }

    private sealed class RejectingEvaluator : IForwardEvidenceEvaluator
    {
        public ValueTask<ForwardEvidenceEvaluation?> EvaluateAsync(
            ForwardEvidencePolicy policy,
            IReadOnlyList<ForwardEvidenceArtifact> artifacts,
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Gap failure must occur before evaluator invocation.");
    }

    private sealed class Repository : IForwardEvidenceRepository
    {
        public List<ForwardEvidenceArtifact> Artifacts { get; } = [];

        public List<ForwardEvidenceEvaluation> Evaluations { get; } = [];

        public Task<IReadOnlyList<ForwardEvidenceArtifact>> ListArtifactsAsync(
            string pipelineId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ForwardEvidenceArtifact>>(Artifacts.ToArray());

        public Task<StoredForwardEvidenceArtifact?> GetArtifactAsync(
            string windowSha256,
            CancellationToken cancellationToken)
        {
            var artifact = Artifacts.SingleOrDefault(item =>
                item.Window.IdentitySha256 == windowSha256);
            return Task.FromResult(artifact is null
                ? null
                : new StoredForwardEvidenceArtifact(
                    artifact.Window.IdentitySha256,
                    artifact.ManifestSha256));
        }

        public Task<StoredForwardEvidenceEvaluation?> GetEvaluationAsync(
            string runSha256,
            CancellationToken cancellationToken)
        {
            var evaluation = Evaluations.SingleOrDefault(item => item.RunSha256 == runSha256);
            return Task.FromResult(evaluation is null
                ? null
                : new StoredForwardEvidenceEvaluation(
                    evaluation.PipelineId,
                    evaluation.SealedWindowCount,
                    evaluation.RunSha256,
                    evaluation.ReportSha256,
                    evaluation.Acceptance.IsAccepted));
        }

        public Task<StoredForwardEvidenceEvaluation?> GetLatestEvaluationAsync(
            string pipelineId,
            CancellationToken cancellationToken)
        {
            var evaluation = Evaluations.LastOrDefault();
            return Task.FromResult(evaluation is null
                ? null
                : new StoredForwardEvidenceEvaluation(
                    evaluation.PipelineId,
                    evaluation.SealedWindowCount,
                    evaluation.RunSha256,
                    evaluation.ReportSha256,
                    evaluation.Acceptance.IsAccepted));
        }

        public void AddArtifact(ForwardEvidenceArtifact artifact) => Artifacts.Add(artifact);

        public void AddEvaluation(ForwardEvidenceEvaluation evaluation) =>
            Evaluations.Add(evaluation);
    }

    private sealed class UnitOfWork : ITradingUnitOfWork
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }
}
