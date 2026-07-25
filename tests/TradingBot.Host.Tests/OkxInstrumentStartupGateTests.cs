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
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteClosedHistoryOpensCandleReadiness()
    {
        var history = new StubHistoryClient(returnCompleteRange: true);
        await using var provider = CreateProvider(history);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        var gate = CreateGate(
            provider.GetRequiredService<IServiceScopeFactory>(),
            readiness);

        await gate.StartAsync(CancellationToken.None);

        var snapshot = readiness.Snapshot;
        Assert.True(snapshot.InstrumentReady);
        Assert.True(snapshot.CandleHistoryReady);
        Assert.False(snapshot.MarketDataReady);
        Assert.False(snapshot.IsReady);
        Assert.Equal("market-data-not-ready", snapshot.Reason);
        Assert.Equal(900, snapshot.CandleTimeframeSeconds);
        Assert.Equal(200, snapshot.WarmupCandleCount);
        Assert.Equal(200, history.RequestedCount);
        Assert.Equal(new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero), history.ToExclusive);
    }

    [Fact]
    public async Task IncompleteHistoryFailsStartupAndKeepsReadinessClosed()
    {
        var history = new StubHistoryClient(returnCompleteRange: false);
        await using var provider = CreateProvider(history);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        var gate = CreateGate(
            provider.GetRequiredService<IServiceScopeFactory>(),
            readiness);

        var action = () => gate.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.True(readiness.Snapshot.InstrumentReady);
        Assert.False(readiness.Snapshot.CandleHistoryReady);
        Assert.False(readiness.Snapshot.IsReady);
        Assert.Equal(
            nameof(DomainRuleViolationException),
            readiness.Snapshot.Reason);
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
        IServiceScopeFactory scopeFactory,
        TradingReadinessState readiness) =>
        new(
            scopeFactory,
            Options.Create(new TradingOptions
            {
                MarketDataSource = MarketDataSource.OkxPublic,
                Exchange = "OKX",
                Symbol = "BTC-USDT",
                CandleTimeframeSeconds = 900,
                WarmupCandleCount = 200
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

    private sealed class StubHistoryClient(bool returnCompleteRange) : IClosedCandleHistoryClient
    {
        public int RequestedCount { get; private set; }

        public DateTimeOffset? ToExclusive { get; private set; }

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            RequestedCount = (int)((toExclusive - fromInclusive).Ticks / timeframe.Duration.Ticks);
            ToExclusive = toExclusive;
            if (!returnCompleteRange)
            {
                return ValueTask.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
            }

            var candles = new Candle[RequestedCount];
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
