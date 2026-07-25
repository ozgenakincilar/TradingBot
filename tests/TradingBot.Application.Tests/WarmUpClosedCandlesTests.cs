using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class WarmUpClosedCandlesTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe OneMinute = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start = new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadsExactLookbackEndingAtLastClosedBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new StubHistoryClient([CandleAt(4), CandleAt(5), CandleAt(6)]);
        var warmup = new WarmUpClosedCandles(client, maximumCandlesPerRequest: 300);

        var result = await warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 3,
            knownAt: Start.AddMinutes(7).AddSeconds(42),
            cancellation.Token);

        Assert.Equal(Start.AddMinutes(4), result.FromInclusive);
        Assert.Equal(Start.AddMinutes(7), result.ToExclusive);
        Assert.Equal(3, result.Candles.Count);
        Assert.Equal(result.FromInclusive, client.FromInclusive);
        Assert.Equal(result.ToExclusive, client.ToExclusive);
        Assert.Equal(cancellation.Token, client.CancellationToken);
    }

    [Fact]
    public async Task ExactKnownBoundaryIncludesCandleThatClosedAtBoundary()
    {
        var client = new StubHistoryClient([CandleAt(5), CandleAt(6)]);
        var warmup = new WarmUpClosedCandles(client, maximumCandlesPerRequest: 300);

        var result = await warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 2,
            knownAt: Start.AddMinutes(7),
            CancellationToken.None);

        Assert.Equal(Start.AddMinutes(7), result.ToExclusive);
        Assert.Equal(Start.AddMinutes(6), result.Candles[^1].OpenTime);
    }

    [Fact]
    public async Task MissingHistoryFailsClosed()
    {
        var warmup = new WarmUpClosedCandles(
            new StubHistoryClient([CandleAt(5)]),
            maximumCandlesPerRequest: 300);

        var action = () => warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 2,
            knownAt: Start.AddMinutes(7),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task ShiftedContiguousHistoryFailsClosed()
    {
        var warmup = new WarmUpClosedCandles(
            new StubHistoryClient([CandleAt(4), CandleAt(5)]),
            maximumCandlesPerRequest: 300);

        var action = () => warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 2,
            knownAt: Start.AddMinutes(7),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task GapInsideHistoryFailsClosed()
    {
        var warmup = new WarmUpClosedCandles(
            new StubHistoryClient([CandleAt(4), CandleAt(6)]),
            maximumCandlesPerRequest: 300);

        var action = () => warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 2,
            knownAt: Start.AddMinutes(7),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task OversizedLookbackIsRejectedBeforeNetworkCall()
    {
        var client = new StubHistoryClient([]);
        var warmup = new WarmUpClosedCandles(client, maximumCandlesPerRequest: 2);

        var action = () => warmup.HandleAsync(
            Instrument,
            OneMinute,
            requiredCandleCount: 3,
            knownAt: Start.AddMinutes(7),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.False(client.WasCalled);
    }

    private static Candle CandleAt(int minute) =>
        Candle.CreateClosed(
            Instrument,
            OneMinute,
            Start.AddMinutes(minute),
            Start.AddMinutes(minute + 1),
            100m,
            110m,
            90m,
            105m,
            12m);

    private sealed class StubHistoryClient(IReadOnlyList<Candle> candles) : IClosedCandleHistoryClient
    {
        public bool WasCalled { get; private set; }

        public DateTimeOffset FromInclusive { get; private set; }

        public DateTimeOffset ToExclusive { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            FromInclusive = fromInclusive;
            ToExclusive = toExclusive;
            CancellationToken = cancellationToken;
            return ValueTask.FromResult(candles);
        }
    }
}
