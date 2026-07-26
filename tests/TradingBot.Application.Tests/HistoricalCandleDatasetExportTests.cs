using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Tests;

public sealed class HistoricalCandleDatasetExportTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportsLargeRangeInBoundedContiguousPages()
    {
        var history = new RecordingHistoryClient();
        var sink = new RecordingSink();
        var useCase = new ExportHistoricalCandleDataset(
            history,
            sink,
            new FixedTimeProvider(Start.AddDays(1)));
        var request = Request(candleCount: 205);

        var artifact = await useCase.ExecuteAsync(request, CancellationToken.None);

        Assert.Equal([100, 100, 5], history.PageSizes);
        Assert.Equal(205, sink.Candles.Count);
        Assert.Equal(Start, sink.Candles[0].OpenTime);
        Assert.Equal(Start.AddMinutes(205), sink.Candles[^1].CloseTime);
        Assert.Equal(205, artifact.Summary.CandleCount);
        Assert.Equal(request.ToExclusive, artifact.Summary.LastCloseTime);
    }

    [Fact]
    public async Task ShortHistoryPageFailsBeforeArtifactCanBePublished()
    {
        var history = new RecordingHistoryClient(returnShortPage: true);
        var sink = new RecordingSink();
        var useCase = new ExportHistoricalCandleDataset(
            history,
            sink,
            new FixedTimeProvider(Start.AddDays(1)));

        var action = () => useCase.ExecuteAsync(Request(candleCount: 101), CancellationToken.None)
            .AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.False(sink.Completed);
    }

    [Fact]
    public async Task NonUtcOrOpenEndedRangeFailsBeforeReadingHistory()
    {
        var history = new RecordingHistoryClient();
        var useCase = new ExportHistoricalCandleDataset(
            history,
            new RecordingSink(),
            new FixedTimeProvider(Start.AddDays(1)));
        var request = Request(candleCount: 1) with
        {
            FromInclusive = Start.ToOffset(TimeSpan.FromHours(3))
        };

        var action = () => useCase.ExecuteAsync(request, CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Empty(history.PageSizes);
    }

    [Fact]
    public async Task CancellationDuringPagePacingPreventsArtifactCompletion()
    {
        var history = new RecordingHistoryClient();
        var sink = new RecordingSink();
        var useCase = new ExportHistoricalCandleDataset(
            history,
            sink,
            TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var action = () => useCase.ExecuteAsync(Request(candleCount: 101), cancellation.Token)
            .AsTask();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Equal([100], history.PageSizes);
        Assert.False(sink.Completed);
    }

    private static HistoricalCandleExportRequest Request(int candleCount) => new(
        Instrument,
        Timeframe,
        Start,
        Start.AddMinutes(candleCount),
        "okx-btc-usdt-1m-research",
        "research.csv");

    private sealed class RecordingHistoryClient(bool returnShortPage = false)
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
            var count = (int)((toExclusive - fromInclusive).Ticks / timeframe.Duration.Ticks);
            PageSizes.Add(count);
            var returnedCount = returnShortPage ? count - 1 : count;
            IReadOnlyList<Candle> candles = Enumerable.Range(0, returnedCount)
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

    private sealed class RecordingSink : IHistoricalCandleDatasetSink
    {
        public List<Candle> Candles { get; } = [];

        public bool Completed { get; private set; }

        public async ValueTask<HistoricalCandleExportArtifact> WriteAsync(
            HistoricalCandleExportRequest request,
            DateTimeOffset exportedAt,
            IAsyncEnumerable<Candle> candles,
            CancellationToken cancellationToken)
        {
            await foreach (var candle in candles.WithCancellation(cancellationToken))
            {
                Candles.Add(candle);
            }

            Completed = true;
            return new HistoricalCandleExportArtifact(
                request.OutputPath,
                exportedAt,
                new HistoricalCandleDatasetDescriptor(
                    request.SourceId,
                    HistoricalCandleDatasetContract.CsvSchemaVersion,
                    new string('A', 64),
                    request.InstrumentId,
                    request.Timeframe),
                new HistoricalCandleDatasetSummary(
                    Candles.Count,
                    Candles[0].OpenTime,
                    Candles[^1].CloseTime));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
