using System.Runtime.CompilerServices;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Infrastructure.Tests;

public sealed class AtomicCsvHistoricalCandleDatasetSinkTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExportedAt = Start.AddDays(1);

    [Fact]
    public async Task PublishesCanonicalDatasetThatCanBeVerifiedByReader()
    {
        var path = NewPath();
        try
        {
            var artifact = await new AtomicCsvHistoricalCandleDatasetSink().WriteAsync(
                Request(path, 3),
                ExportedAt,
                Candles(3),
                CancellationToken.None);

            Assert.True(File.Exists(path));
            await using var dataset = await CsvHistoricalCandleDataset.OpenAsync(
                path,
                artifact.Descriptor.SourceId,
                Instrument,
                Timeframe,
                ExportedAt,
                CancellationToken.None);
            var read = new List<Candle>();
            await foreach (var candle in dataset.ReadAsync(CancellationToken.None))
            {
                read.Add(candle);
            }

            Assert.Equal(3, read.Count);
            Assert.Equal(artifact.Descriptor.Sha256, dataset.Descriptor.Sha256);
            Assert.Equal(artifact.Summary, dataset.CompletedSummary);
            var rawBytes = await File.ReadAllBytesAsync(path);
            Assert.False(rawBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.DoesNotContain((byte)'\r', rawBytes);
            Assert.Empty(PartialFiles(path));
        }
        finally
        {
            DeleteArtifacts(path);
        }
    }

    [Fact]
    public async Task FailedStreamLeavesNeitherTargetNorPartialFile()
    {
        var path = NewPath();
        try
        {
            var action = () => new AtomicCsvHistoricalCandleDatasetSink().WriteAsync(
                Request(path, 2),
                ExportedAt,
                FailingCandles(),
                CancellationToken.None).AsTask();

            await Assert.ThrowsAsync<InvalidOperationException>(action);
            Assert.False(File.Exists(path));
            Assert.Empty(PartialFiles(path));
        }
        finally
        {
            DeleteArtifacts(path);
        }
    }

    [Fact]
    public async Task ExistingDatasetIsNeverOverwritten()
    {
        var path = NewPath();
        try
        {
            await File.WriteAllTextAsync(path, "existing");
            var action = () => new AtomicCsvHistoricalCandleDatasetSink().WriteAsync(
                Request(path, 1),
                ExportedAt,
                Candles(1),
                CancellationToken.None).AsTask();

            await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(action);
            Assert.Equal("existing", await File.ReadAllTextAsync(path));
            Assert.Empty(PartialFiles(path));
        }
        finally
        {
            DeleteArtifacts(path);
        }
    }

    private static HistoricalCandleExportRequest Request(string path, int candleCount) => new(
        Instrument,
        Timeframe,
        Start,
        Start + (Timeframe.Duration * candleCount),
        "okx-btc-usdt-15m-export",
        path);

    private static async IAsyncEnumerable<Candle> Candles(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return CandleAt(index);
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<Candle> FailingCandles(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return CandleAt(0);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        throw new InvalidOperationException("fixture stream failed");
    }

    private static Candle CandleAt(int index) => Candle.CreateClosed(
        Instrument,
        Timeframe,
        Start + (Timeframe.Duration * index),
        ExportedAt,
        100m,
        101m,
        99m,
        100m,
        10m);

    private static string NewPath() => Path.Combine(
        Path.GetTempPath(),
        $"tradingbot-export-{Guid.NewGuid():N}.csv");

    private static string[] PartialFiles(string path) => Directory.GetFiles(
        Path.GetDirectoryName(path)!,
        $"{Path.GetFileName(path)}.partial-*");

    private static void DeleteArtifacts(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        foreach (var partial in PartialFiles(path))
        {
            File.Delete(partial);
        }
    }
}
