using System.Runtime.CompilerServices;
using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class ClosedCandleStreamSessionTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe FifteenMinutes = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe OneHour = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 12, 7, 0, TimeSpan.Zero);

    [Fact]
    public async Task RestAnchorsBothTimeframesBeforePublishingStreamCandles()
    {
        var stream = new StubStreamClient([
            CandleAt(FifteenMinutes, new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero)),
            CandleAt(OneHour, new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero))
        ]);
        var session = CreateSession(stream, new GeneratingHistoryClient());

        var updates = await ReadAllAsync(session, [FifteenMinutes, OneHour]);

        Assert.Equal(
            [
                ClosedCandleStreamUpdateKind.SessionReady,
                ClosedCandleStreamUpdateKind.Candle,
                ClosedCandleStreamUpdateKind.Candle
            ],
            updates.Select(update => update.Kind));
        Assert.Equal(FifteenMinutes, updates[1].Candle?.Timeframe);
        Assert.Equal(OneHour, updates[2].Candle?.Timeframe);
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderCandlesAreSuppressed()
    {
        var duplicate = CandleAt(
            FifteenMinutes,
            new DateTimeOffset(2026, 7, 25, 11, 45, 0, TimeSpan.Zero));
        var older = CandleAt(
            FifteenMinutes,
            new DateTimeOffset(2026, 7, 25, 11, 30, 0, TimeSpan.Zero));
        var next = CandleAt(
            FifteenMinutes,
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var session = CreateSession(
            new StubStreamClient([duplicate, older, next]),
            new GeneratingHistoryClient());

        var updates = await ReadAllAsync(session, [FifteenMinutes]);

        Assert.Equal(2, updates.Count);
        Assert.Equal(ClosedCandleStreamUpdateKind.SessionReady, updates[0].Kind);
        Assert.Equal(next, updates[1].Candle);
    }

    [Fact]
    public async Task GapIsRecoveredFromRestAndPublishedInOrder()
    {
        var observed = CandleAt(
            FifteenMinutes,
            new DateTimeOffset(2026, 7, 25, 12, 15, 0, TimeSpan.Zero));
        var history = new GeneratingHistoryClient();
        var session = CreateSession(new StubStreamClient([observed]), history);

        var updates = await ReadAllAsync(session, [FifteenMinutes]);

        Assert.Equal(3, updates.Count);
        Assert.Equal(
            [
                new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 25, 12, 15, 0, TimeSpan.Zero)
            ],
            updates.Skip(1).Select(update => update.Candle!.OpenTime));
        Assert.Equal(2, history.Requests.Count);
    }

    [Fact]
    public async Task OversizedGapTerminatesSessionFailClosed()
    {
        var observed = CandleAt(
            FifteenMinutes,
            new DateTimeOffset(2026, 7, 25, 12, 30, 0, TimeSpan.Zero));
        var session = new ClosedCandleStreamSession(
            new StubStreamClient([observed]),
            new GeneratingHistoryClient(),
            new SequencedTimeProvider(Now, Now.AddHours(1)),
            maximumCandlesPerRecovery: 2);

        var action = () => ReadAllAsync(session, [FifteenMinutes]);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private static ClosedCandleStreamSession CreateSession(
        IClosedCandleStreamClient stream,
        IClosedCandleHistoryClient history) =>
        new(
            stream,
            history,
            new SequencedTimeProvider(Now, Now.AddHours(1)),
            maximumCandlesPerRecovery: 16);

    private static async Task<List<ClosedCandleStreamUpdate>> ReadAllAsync(
        ClosedCandleStreamSession session,
        IReadOnlyCollection<Timeframe> timeframes)
    {
        var updates = new List<ClosedCandleStreamUpdate>();
        await foreach (var update in session.ReadValidatedAsync(
                           Instrument,
                           timeframes,
                           CancellationToken.None))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static Candle CandleAt(Timeframe timeframe, DateTimeOffset openTime) =>
        Candle.CreateClosed(
            Instrument,
            timeframe,
            openTime,
            Now.AddHours(2),
            100m,
            101m,
            99m,
            100m,
            1m);

    private sealed class StubStreamClient(IReadOnlyCollection<Candle> candles)
        : IClosedCandleStreamClient
    {
        public async IAsyncEnumerable<Candle> ReadClosedAsync(
            InstrumentId instrumentId,
            IReadOnlyCollection<Timeframe> timeframes,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var candle in candles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return candle;
                await Task.Yield();
            }
        }
    }

    private sealed class GeneratingHistoryClient : IClosedCandleHistoryClient
    {
        public List<(Timeframe Timeframe, DateTimeOffset From, DateTimeOffset To)> Requests { get; } = [];

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            Requests.Add((timeframe, fromInclusive, toExclusive));
            var count = (int)((toExclusive - fromInclusive).Ticks / timeframe.Duration.Ticks);
            var candles = new Candle[count];
            for (var index = 0; index < count; index++)
            {
                candles[index] = CandleAt(
                    timeframe,
                    fromInclusive + (timeframe.Duration * index));
            }

            return ValueTask.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }

    private sealed class SequencedTimeProvider(
        DateTimeOffset first,
        DateTimeOffset subsequent) : TimeProvider
    {
        private int _calls;

        public override DateTimeOffset GetUtcNow() =>
            Interlocked.Increment(ref _calls) == 1 ? first : subsequent;
    }
}
