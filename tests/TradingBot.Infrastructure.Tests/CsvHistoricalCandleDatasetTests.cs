using System.Globalization;
using System.Text;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Infrastructure.Tests;

public sealed class CsvHistoricalCandleDatasetTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Timeframe = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly DateTimeOffset Start =
        new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CanonicalCsvStreamsContiguousCandlesAndProducesStableHash()
    {
        var path = await CreateCsvAsync(candleCount: 3);
        try
        {
            await using var first = await OpenAsync(path, knownAtIndex: 4);
            var candles = await ReadAllAsync(first);
            await using var second = await OpenAsync(path, knownAtIndex: 4);

            Assert.Equal(3, candles.Count);
            Assert.Equal(3, first.CompletedSummary?.CandleCount);
            Assert.Equal(Start, first.CompletedSummary?.FirstOpenTime);
            Assert.Equal(Start + (Timeframe.Duration * 3), first.CompletedSummary?.LastCloseTime);
            Assert.Equal(64, first.Descriptor.Sha256.Length);
            Assert.Equal(first.Descriptor.Sha256, second.Descriptor.Sha256);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GapFailsClosedWithoutCompletedSummary()
    {
        var path = await CreateCsvAsync(candleCount: 3, skippedIndex: 1);
        try
        {
            await using var dataset = await OpenAsync(path, knownAtIndex: 4);

            var action = () => ReadAllAsync(dataset);

            await Assert.ThrowsAsync<DomainRuleViolationException>(action);
            Assert.Null(dataset.CompletedSummary);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task EarlyConsumerStopCannotProduceCompletedDatasetEvidence()
    {
        var path = await CreateCsvAsync(candleCount: 3);
        try
        {
            await using var dataset = await OpenAsync(path, knownAtIndex: 4);
            await foreach (var _ in dataset.ReadAsync(CancellationToken.None))
            {
                break;
            }

            Assert.Null(dataset.CompletedSummary);
            Assert.Throws<InvalidOperationException>(() =>
                dataset.ReadAsync(CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LargeFixtureIsConsumedAsSinglePassAsyncStream()
    {
        const int candleCount = 25_000;
        var path = await CreateCsvAsync(candleCount);
        try
        {
            await using var dataset = await OpenAsync(path, candleCount + 1);
            var observed = 0;
            await foreach (var _ in dataset.ReadAsync(CancellationToken.None))
            {
                observed++;
            }

            Assert.Equal(candleCount, observed);
            Assert.Equal(candleCount, dataset.CompletedSummary?.CandleCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FactoryOpensFreshSingleUseStreamForEveryWindowPass()
    {
        var path = await CreateCsvAsync(candleCount: 3);
        try
        {
            var factory = new CsvHistoricalCandleDatasetFactory(
            [
                new CsvHistoricalCandleDatasetRegistration(
                    Instrument,
                    Timeframe,
                    path,
                    "okx-btc-usdt-15m-fixture",
                    Start + (Timeframe.Duration * 4))
            ]);
            await using var first = await factory.OpenAsync(
                Instrument,
                Timeframe,
                CancellationToken.None);
            await ReadAllAsync(first);
            await using var second = await factory.OpenAsync(
                Instrument,
                Timeframe,
                CancellationToken.None);
            await ReadAllAsync(second);

            Assert.Equal(first.Descriptor.Sha256, second.Descriptor.Sha256);
            Assert.Equal(3, first.CompletedSummary?.CandleCount);
            Assert.Equal(3, second.CompletedSummary?.CandleCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ValueTask<CsvHistoricalCandleDataset> OpenAsync(
        string path,
        int knownAtIndex) =>
        CsvHistoricalCandleDataset.OpenAsync(
            path,
            "okx-btc-usdt-15m-fixture",
            Instrument,
            Timeframe,
            Start + (Timeframe.Duration * knownAtIndex),
            CancellationToken.None);

    private static async Task<List<Candle>> ReadAllAsync(IHistoricalCandleDataset dataset)
    {
        var candles = new List<Candle>();
        await foreach (var candle in dataset.ReadAsync(CancellationToken.None))
        {
            candles.Add(candle);
        }

        return candles;
    }

    private static async Task<string> CreateCsvAsync(
        int candleCount,
        int? skippedIndex = null)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"tradingbot-{Guid.NewGuid():N}.csv");
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65_536,
            useAsync: true);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(HistoricalCandleDatasetContract.CsvHeader);
        for (var index = 0; index < candleCount; index++)
        {
            if (index == skippedIndex)
            {
                continue;
            }

            var openTime = Start + (Timeframe.Duration * index);
            await writer.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{openTime:O},100,101,99,100,1"));
        }

        return path;
    }
}
