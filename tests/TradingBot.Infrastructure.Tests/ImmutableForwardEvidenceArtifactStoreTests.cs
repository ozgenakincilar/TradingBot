using System.Security.Cryptography;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Infrastructure.Tests;

public sealed class ImmutableForwardEvidenceArtifactStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"tradingbot-forward-evidence-{Guid.NewGuid():N}");

    [Fact]
    public async Task CompleteWindowIsAtomicallySealedAndReopenedByHash()
    {
        var policy = Policy();
        var history = new History();
        var store = new ImmutableForwardEvidenceArtifactStore(
            _root,
            history,
            new Clock(Start.AddDays(30)));

        var first = await store.SealAsync(
            policy,
            policy.GetWindow(0),
            CancellationToken.None);
        var second = await store.SealAsync(
            policy,
            policy.GetWindow(0),
            CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(2_880, first.Signal.CandleCount);
        Assert.Equal(720, first.Trend.CandleCount);
        Assert.Equal(await HashAsync(first.Signal.FilePath), first.Signal.Sha256);
        Assert.Equal(await HashAsync(first.Trend.FilePath), first.Trend.Sha256);
        Assert.Equal(await HashAsync(first.ManifestPath), first.ManifestSha256);
        Assert.True(File.GetAttributes(first.ManifestPath).HasFlag(FileAttributes.ReadOnly));
        Assert.Equal(37, history.RequestCount);
    }

    [Fact]
    public async Task IncompleteHistoryCannotPublishAnEvidenceWindow()
    {
        var policy = Policy();
        var store = new ImmutableForwardEvidenceArtifactStore(
            _root,
            new History(dropLastCandle: true),
            new Clock(Start.AddDays(30)));

        await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(async () =>
            await store.SealAsync(
                policy,
                policy.GetWindow(0),
                CancellationToken.None));

        Assert.Empty(Directory.Exists(_root)
            ? Directory.EnumerateFiles(_root, "manifest.json", SearchOption.AllDirectories)
            : []);
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
        "btc-usdt-v6-forward",
        InstrumentId.Create("OKX", "BTC-USDT"),
        Timeframe.Create(TimeSpan.FromMinutes(15)),
        Timeframe.Create(TimeSpan.FromHours(1)),
        Start);

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class History(bool dropLastCandle = false) : IClosedCandleHistoryClient
    {
        public int RequestCount { get; private set; }

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            var expectedCount = (int)((toExclusive - fromInclusive).Ticks /
                timeframe.Duration.Ticks);
            var count = dropLastCandle && RequestCount == 1
                ? expectedCount - 1
                : expectedCount;
            var candles = new Candle[count];
            var cursor = fromInclusive;
            for (var index = 0; index < count; index++)
            {
                candles[index] = Candle.CreateClosed(
                    instrumentId,
                    timeframe,
                    cursor,
                    Start.AddDays(30),
                    100m,
                    101m,
                    99m,
                    100m,
                    1_000m);
                cursor = cursor.Add(timeframe.Duration);
            }

            return ValueTask.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }
}
