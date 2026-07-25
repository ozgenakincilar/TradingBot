using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class RecoverClosedCandleGapTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe OneMinute = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start = new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteClosedRangeIsReturnedInOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new StubHistoryClient([CandleAt(1), CandleAt(2), CandleAt(3)]);
        var service = new RecoverClosedCandleGap(client, maximumCandlesPerRecovery: 10);

        var result = await service.HandleAsync(
            Instrument,
            OneMinute,
            Start.AddMinutes(1),
            Start.AddMinutes(3),
            Start.AddMinutes(4),
            cancellation.Token);

        Assert.Equal(3, result.Count);
        Assert.Equal(Start.AddMinutes(1), client.FromInclusive);
        Assert.Equal(Start.AddMinutes(4), client.ToExclusive);
        Assert.Equal(cancellation.Token, client.CancellationToken);
    }

    [Fact]
    public async Task MissingCandleRejectsEntireRecovery()
    {
        var service = new RecoverClosedCandleGap(
            new StubHistoryClient([CandleAt(1), CandleAt(3)]),
            maximumCandlesPerRecovery: 10);

        var action = () => service.HandleAsync(
            Instrument,
            OneMinute,
            Start.AddMinutes(1),
            Start.AddMinutes(3),
            Start.AddMinutes(4),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task OutOfOrderResponseRejectsEntireRecovery()
    {
        var service = new RecoverClosedCandleGap(
            new StubHistoryClient([CandleAt(2), CandleAt(1)]),
            maximumCandlesPerRecovery: 10);

        var action = () => service.HandleAsync(
            Instrument,
            OneMinute,
            Start.AddMinutes(1),
            Start.AddMinutes(2),
            Start.AddMinutes(3),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task OpenObservedCandleCannotBeRequested()
    {
        var service = new RecoverClosedCandleGap(
            new StubHistoryClient([]),
            maximumCandlesPerRecovery: 10);

        var action = () => service.HandleAsync(
            Instrument,
            OneMinute,
            Start.AddMinutes(1),
            Start.AddMinutes(2),
            Start.AddMinutes(2).AddSeconds(59),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task OversizedGapIsRejectedBeforeCallingExchange()
    {
        var client = new StubHistoryClient([]);
        var service = new RecoverClosedCandleGap(client, maximumCandlesPerRecovery: 2);

        var action = () => service.HandleAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(2),
            Start.AddMinutes(3),
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
