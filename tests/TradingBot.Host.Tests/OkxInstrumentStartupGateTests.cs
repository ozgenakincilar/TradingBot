using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Host.Tests;

public sealed class OkxInstrumentStartupGateTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe SignalTimeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe TrendTimeframe = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteSignalAndTrendHistoryOpenCandleReadiness()
    {
        var history = new StubHistoryClient();
        await using var provider = CreateProvider(history);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        var gate = CreateGate(provider, readiness);

        await gate.StartAsync(CancellationToken.None);

        var snapshot = readiness.Snapshot;
        Assert.True(snapshot.InstrumentReady);
        Assert.True(snapshot.SignalCandleHistoryReady);
        Assert.True(snapshot.TrendCandleHistoryReady);
        Assert.True(snapshot.CandleHistoryReady);
        Assert.False(snapshot.MarketDataReady);
        Assert.False(snapshot.IsReady);
        Assert.Equal("market-data-not-ready", snapshot.Reason);
        Assert.Collection(
            history.Requests,
            request =>
            {
                Assert.Equal(SignalTimeframe, request.Timeframe);
                Assert.Equal(200, request.Count);
                Assert.Equal(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), request.ToExclusive);
            },
            request =>
            {
                Assert.Equal(TrendTimeframe, request.Timeframe);
                Assert.Equal(200, request.Count);
                Assert.Equal(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), request.ToExclusive);
            });
    }

    [Fact]
    public async Task IncompleteSignalHistoryFailsBeforeTrendRequest()
    {
        var history = new StubHistoryClient(incompleteTimeframe: SignalTimeframe);
        await using var provider = CreateProvider(history);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        var gate = CreateGate(provider, readiness);

        var action = () => gate.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.True(readiness.Snapshot.InstrumentReady);
        Assert.False(readiness.Snapshot.SignalCandleHistoryReady);
        Assert.False(readiness.Snapshot.TrendCandleHistoryReady);
        Assert.Single(history.Requests);
        Assert.Equal(nameof(DomainRuleViolationException), readiness.Snapshot.Reason);
    }

    [Fact]
    public async Task IncompleteTrendHistoryPreservesSignalEvidenceButKeepsReadinessClosed()
    {
        var history = new StubHistoryClient(incompleteTimeframe: TrendTimeframe);
        await using var provider = CreateProvider(history);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        var gate = CreateGate(provider, readiness);

        var action = () => gate.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.True(readiness.Snapshot.SignalCandleHistoryReady);
        Assert.False(readiness.Snapshot.TrendCandleHistoryReady);
        Assert.False(readiness.Snapshot.CandleHistoryReady);
        Assert.Equal(2, history.Requests.Count);
        Assert.Equal(nameof(DomainRuleViolationException), readiness.Snapshot.Reason);
    }

    private static ServiceProvider CreateProvider(IClosedCandleHistoryClient history)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISpotInstrumentCatalog>(new StubCatalog());
        services.AddTransient<EnsureSpotInstrumentTradable>();
        services.AddSingleton(history);
        services.AddTransient(serviceProvider => new WarmUpClosedCandles(
            serviceProvider.GetRequiredService<IClosedCandleHistoryClient>(),
            maximumCandlesPerRequest: 300));
        return services.BuildServiceProvider();
    }

    private static OkxInstrumentStartupGate CreateGate(
        ServiceProvider provider,
        TradingReadinessState readiness) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TradingOptions
            {
                MarketDataSource = MarketDataSource.OkxPublic,
                Exchange = "OKX",
                Symbol = "BTC-USDT",
                SignalCandleTimeframeSeconds = 900,
                SignalWarmupCandleCount = 200,
                TrendCandleTimeframeSeconds = 3600,
                TrendWarmupCandleCount = 200
            }),
            new FixedTimeProvider(Now),
            readiness,
            NullLogger<OkxInstrumentStartupGate>.Instance);

    private sealed class StubCatalog : ISpotInstrumentCatalog
    {
        public ValueTask<SpotInstrumentMetadata> GetAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SpotInstrumentMetadata(
                instrumentId,
                AssetCode.Create("BTC"),
                AssetCode.Create("USDT"),
                0.1m,
                0.00000001m,
                0.00001m,
                true,
                "live"));
    }

    private sealed class StubHistoryClient(Timeframe? incompleteTimeframe = null)
        : IClosedCandleHistoryClient
    {
        public List<HistoryRequest> Requests { get; } = [];

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            var count = (int)((toExclusive - fromInclusive).Ticks / timeframe.Duration.Ticks);
            Requests.Add(new HistoryRequest(timeframe, count, toExclusive));
            if (timeframe == incompleteTimeframe)
            {
                return ValueTask.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
            }

            var candles = new Candle[count];
            for (var index = 0; index < candles.Length; index++)
            {
                candles[index] = Candle.CreateClosed(
                    instrumentId,
                    timeframe,
                    fromInclusive + (timeframe.Duration * index),
                    Now,
                    100m,
                    101m,
                    99m,
                    100m,
                    1m);
            }

            return ValueTask.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }

    private sealed record HistoryRequest(
        Timeframe Timeframe,
        int Count,
        DateTimeOffset ToExclusive);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
