using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class PagedClosedCandleHistoryClientTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TwoHundredCandleWarmupUsesTwoOfficialPages()
    {
        var inner = new RecordingClient();
        var paged = new PagedClosedCandleHistoryClient(inner, maximumPageSize: 100);

        var result = await paged.GetAsync(
            Instrument,
            Timeframe,
            Start,
            Start.AddMinutes(200),
            CancellationToken.None);

        Assert.Equal([100, 100], inner.PageSizes);
        Assert.Equal(200, result.Count);
        Assert.Equal(Start, result[0].OpenTime);
        Assert.Equal(Start.AddMinutes(200), result[^1].CloseTime);
    }

    [Fact]
    public async Task ShortSecondPageFailsClosed()
    {
        var paged = new PagedClosedCandleHistoryClient(
            new RecordingClient(shortSecondPage: true),
            maximumPageSize: 100);

        var action = () => paged.GetAsync(
            Instrument,
            Timeframe,
            Start,
            Start.AddMinutes(200),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private sealed class RecordingClient(bool shortSecondPage = false)
        : IClosedCandleHistoryClient
    {
        public List<int> PageSizes { get; } = [];

        public ValueTask<IReadOnlyList<Candle>> GetAsync(
            InstrumentId instrumentId,
            Timeframe timeframe,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)((toExclusive - fromInclusive).Ticks / timeframe.Duration.Ticks);
            PageSizes.Add(requested);
            var count = shortSecondPage && PageSizes.Count == 2 ? requested - 1 : requested;
            IReadOnlyList<Candle> candles = Enumerable.Range(0, count)
                .Select(index => Candle.CreateClosed(
                    instrumentId,
                    timeframe,
                    fromInclusive + (timeframe.Duration * index),
                    Start.AddDays(1),
                    100m,
                    101m,
                    99m,
                    100m,
                    10m))
                .ToArray();
            return ValueTask.FromResult(candles);
        }
    }
}
