using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Backtesting;

public sealed class AtomicCsvHistoricalCandleDatasetSink : IHistoricalCandleDatasetSink
{
    private const int BufferSize = 65_536;

    public async ValueTask<HistoricalCandleExportArtifact> WriteAsync(
        HistoricalCandleExportRequest request,
        DateTimeOffset exportedAt,
        IAsyncEnumerable<Candle> candles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candles);
        var targetPath = ValidateTarget(request.OutputPath);
        var temporaryPath = $"{targetPath}.partial-{Guid.NewGuid():N}";
        var published = false;
        try
        {
            var summary = await WriteTemporaryAsync(
                temporaryPath,
                request,
                candles,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var sha256 = await HashAsync(temporaryPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, targetPath, overwrite: false);
            published = true;
            var descriptor = new HistoricalCandleDatasetDescriptor(
                request.SourceId,
                HistoricalCandleDatasetContract.CsvSchemaVersion,
                sha256,
                request.InstrumentId,
                request.Timeframe);
            HistoricalCandleDatasetContract.ValidateDescriptor(descriptor);
            return new HistoricalCandleExportArtifact(
                targetPath,
                exportedAt,
                descriptor,
                summary);
        }
        finally
        {
            if (!published && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<HistoricalCandleDatasetSummary> WriteTemporaryAsync(
        string temporaryPath,
        HistoricalCandleExportRequest request,
        IAsyncEnumerable<Candle> candles,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            BufferSize,
            leaveOpen: false)
        {
            NewLine = "\n"
        };
        await writer.WriteLineAsync(
            HistoricalCandleDatasetContract.CsvHeader.AsMemory(),
            cancellationToken);

        long count = 0;
        Candle? previous = null;
        DateTimeOffset firstOpen = default;
        await foreach (var candle in candles.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candle.InstrumentId != request.InstrumentId ||
                candle.Timeframe != request.Timeframe ||
                (previous is not null && candle.OpenTime != previous.CloseTime))
            {
                throw new DomainRuleViolationException(
                    "Historical candle CSV output must be contiguous and match its identity.");
            }

            if (count == 0)
            {
                firstOpen = candle.OpenTime;
            }

            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{candle.OpenTime:O},{candle.Open:G29},{candle.High:G29},{candle.Low:G29},{candle.Close:G29},{candle.BaseVolume:G29}");
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            previous = candle;
            try
            {
                count = checked(count + 1);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "Historical candle CSV output count overflowed.");
            }
        }

        if (count == 0 || previous is null)
        {
            throw new DomainRuleViolationException(
                "Historical candle CSV output cannot be empty.");
        }

        await writer.FlushAsync(cancellationToken);
        return new HistoricalCandleDatasetSummary(count, firstOpen, previous.CloseTime);
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static string ValidateTarget(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new DomainRuleViolationException(
                "Historical candle CSV output path is required.");
        }

        var targetPath = Path.GetFullPath(outputPath);
        if (!string.Equals(Path.GetExtension(targetPath), ".csv", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(Path.GetDirectoryName(targetPath)) ||
            File.Exists(targetPath))
        {
            throw new DomainRuleViolationException(
                "Historical candle CSV target must be a new file in an existing directory.");
        }

        return targetPath;
    }
}
