using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class ForwardEvidencePipelineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PolicyCountsOnlyFullyClosedThirtyDayWindows()
    {
        var policy = Policy();

        Assert.Equal(0, policy.GetCompletedWindowCount(Start.AddDays(29)));
        Assert.Equal(1, policy.GetCompletedWindowCount(Start.AddDays(30)));
        Assert.Equal(2, policy.GetCompletedWindowCount(Start.AddDays(60).AddMinutes(1)));
        Assert.Equal(policy.GetWindow(0).EndExclusive, policy.GetWindow(1).StartInclusive);
        Assert.NotEqual(policy.GetWindow(0).IdentitySha256, policy.GetWindow(1).IdentitySha256);
    }

    [Fact]
    public async Task PipelineSealsAtMostOneMissingWindowPerCycleAndIsIdempotent()
    {
        var policy = Policy();
        var repository = new Repository();
        var store = new Store();
        var evaluator = new Evaluator();
        var pipeline = new ForwardEvidencePipeline(
            store,
            evaluator,
            repository,
            new UnitOfWork(),
            new Clock(Start.AddDays(60)));

        var first = await pipeline.RunOnceAsync(policy, CancellationToken.None);
        var second = await pipeline.RunOnceAsync(policy, CancellationToken.None);
        var third = await pipeline.RunOnceAsync(policy, CancellationToken.None);

        Assert.True(first.WindowSealed);
        Assert.Equal(1, first.SealedWindowCount);
        Assert.True(second.WindowSealed);
        Assert.Equal(2, second.SealedWindowCount);
        Assert.False(third.WindowSealed);
        Assert.Equal(2, third.SealedWindowCount);
        Assert.Equal(2, store.Calls);
        Assert.Equal([0, 1], repository.Artifacts.Select(static item => item.Window.Index));
    }

    [Fact]
    public void LockedConfigurationPreservesV6AcceptanceParameters()
    {
        var instrumentId = InstrumentId.Create("OKX", "BTC-USDT");
        var rules = Instrument.Create(instrumentId, 0.1m, 0.00000001m, 0.00001m, 1m);

        var locked = LockedAtrHysteresisV6Configuration.Create(rules);

        Assert.Equal(5, locked.Baseline.Version);
        Assert.Equal(30m, locked.Baseline.SignalEmaHysteresisBasisPoints);
        Assert.Equal(6, locked.Candidate.Version);
        Assert.Equal(0m, locked.Candidate.SignalEmaHysteresisBasisPoints);
        Assert.Equal(14, locked.Candidate.SignalAtrPeriod);
        Assert.Equal(0.2m, locked.Candidate.SignalAtrHysteresisMultiplier);
        Assert.Equal(9, locked.ParameterGrid.Count);
        Assert.Equal(0.05m,
            locked.ExecutionPolicy.PaperExecution.MaximumLiquidityParticipation.Fraction);
        Assert.Equal(4, locked.ExecutionPolicy.DynamicExecution!.Value.TwapChildOrderCount);
    }

    private static ForwardEvidencePolicy Policy() => new(
        "btc-usdt-v6-forward",
        InstrumentId.Create("OKX", "BTC-USDT"),
        Timeframe.Create(TimeSpan.FromMinutes(15)),
        Timeframe.Create(TimeSpan.FromHours(1)),
        Start);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Store : IForwardEvidenceArtifactStore
    {
        public int Calls { get; private set; }

        public ValueTask<ForwardEvidenceArtifact> SealAsync(
            ForwardEvidencePolicy policy,
            ForwardEvidenceWindow window,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(Artifact(policy, window));
        }
    }

    private sealed class Evaluator : IForwardEvidenceEvaluator
    {
        public ValueTask<ForwardEvidenceEvaluation?> EvaluateAsync(
            ForwardEvidencePolicy policy,
            IReadOnlyList<ForwardEvidenceArtifact> artifacts,
            DateTimeOffset evaluatedAt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ForwardEvidenceEvaluation?>(null);
        }
    }

    private sealed class Repository : IForwardEvidenceRepository
    {
        public List<ForwardEvidenceArtifact> Artifacts { get; } = [];

        public Task<IReadOnlyList<ForwardEvidenceArtifact>> ListArtifactsAsync(
            string pipelineId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ForwardEvidenceArtifact>>(
                Artifacts.OrderBy(static artifact => artifact.Window.Index).ToArray());

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
            CancellationToken cancellationToken) => Task.FromResult<StoredForwardEvidenceEvaluation?>(null);

        public Task<StoredForwardEvidenceEvaluation?> GetLatestEvaluationAsync(
            string pipelineId,
            CancellationToken cancellationToken) => Task.FromResult<StoredForwardEvidenceEvaluation?>(null);

        public void AddArtifact(ForwardEvidenceArtifact artifact) => Artifacts.Add(artifact);

        public void AddEvaluation(ForwardEvidenceEvaluation evaluation) =>
            throw new InvalidOperationException();
    }

    private sealed class UnitOfWork : ITradingUnitOfWork
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    private static ForwardEvidenceArtifact Artifact(
        ForwardEvidencePolicy policy,
        ForwardEvidenceWindow window) =>
        new(
            policy.PipelineId,
            window,
            $"manifest-{window.Index}.json",
            new string('A', 64),
            new ForwardEvidenceDatasetArtifact(
                $"signal-{window.Index}.csv",
                $"signal-{window.Index}",
                new string('B', 64),
                2_880,
                policy.SignalTimeframe),
            new ForwardEvidenceDatasetArtifact(
                $"trend-{window.Index}.csv",
                $"trend-{window.Index}",
                new string('C', 64),
                720,
                policy.TrendTimeframe),
            window.EndExclusive);
}
