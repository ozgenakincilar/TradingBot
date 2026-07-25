using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Host.Tests;

public sealed class OkxCandleWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidatedLiveSessionReopensBothCandleReadinessGates()
    {
        var stream = new BlockingStreamClient();
        await using var provider = CreateProvider(stream);
        var readiness = new TradingReadinessState(candleHistoryRequired: true);
        readiness.MarkInstrumentReady("OKX:BTC-USDT");
        readiness.MarkMarketDataReady();
        var worker = new OkxCandleWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(Settings()),
            readiness,
            NullLogger<OkxCandleWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => readiness.Snapshot.CandleHistoryReady,
            TimeSpan.FromSeconds(2));

        Assert.True(readiness.Snapshot.IsReady);
        Assert.Equal(2, stream.RequestedTimeframes?.Count);

        await worker.StopAsync(CancellationToken.None);
    }

    private static ServiceProvider CreateProvider(IClosedCandleStreamClient stream)
    {
        var services = new ServiceCollection();
        services.AddSingleton(stream);
        services.AddSingleton<IClosedCandleHistoryClient>(new GeneratingHistoryClient());
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddTransient(serviceProvider => new ClosedCandleStreamSession(
            serviceProvider.GetRequiredService<IClosedCandleStreamClient>(),
            serviceProvider.GetRequiredService<IClosedCandleHistoryClient>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            maximumCandlesPerRecovery: 300));
        return services.BuildServiceProvider();
    }

    private static TradingOptions Settings() => new()
    {
        MarketDataSource = MarketDataSource.OkxPublic,
        Exchange = "OKX",
        Symbol = "BTC-USDT",
        SignalCandleTimeframeSeconds = 900,
        SignalWarmupCandleCount = 200,
        TrendCandleTimeframeSeconds = 3600,
        TrendWarmupCandleCount = 200
    };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
    }

    private sealed class BlockingStreamClient : IClosedCandleStreamClient
    {
        public IReadOnlyCollection<Timeframe>? RequestedTimeframes { get; private set; }

        public async IAsyncEnumerable<Candle> ReadClosedAsync(
            InstrumentId instrumentId,
            IReadOnlyCollection<Timeframe> timeframes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequestedTimeframes = timeframes.ToArray();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class GeneratingHistoryClient : IClosedCandleHistoryClient
    {
        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<Candle>>([
                Candle.CreateClosed(
                    instrumentId,
                    timeframe,
                    fromInclusive,
                    Now,
                    100m,
                    101m,
                    99m,
                    100m,
                    1m)
            ]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
