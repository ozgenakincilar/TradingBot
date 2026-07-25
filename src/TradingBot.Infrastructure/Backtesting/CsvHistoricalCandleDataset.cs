using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Infrastructure.Backtesting;

public sealed class CsvHistoricalCandleDataset : IHistoricalCandleDataset
{
    private const int MaximumLineLength = 512;
    private readonly FileStream _stream;
    private readonly DateTimeOffset _knownAt;
    private int _readStarted;

    private CsvHistoricalCandleDataset(
        FileStream stream,
        DateTimeOffset knownAt,
        HistoricalCandleDatasetDescriptor descriptor)
    {
        _stream = stream;
        _knownAt = knownAt;
        Descriptor = descriptor;
    }

    public HistoricalCandleDatasetDescriptor Descriptor { get; }

    public HistoricalCandleDatasetSummary? CompletedSummary { get; private set; }

    public static async ValueTask<CsvHistoricalCandleDataset> OpenAsync(
        string path,
        string sourceId,
        InstrumentId instrumentId,
        Timeframe timeframe,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (knownAt == default || knownAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Historical dataset known-at time must be UTC.");
        }

        HistoricalCandleDatasetContract.ValidateDescriptor(
            new HistoricalCandleDatasetDescriptor(
                sourceId,
                HistoricalCandleDatasetContract.CsvSchemaVersion,
                new string('0', 64),
                instrumentId,
                timeframe));

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainRuleViolationException("Historical candle dataset must be a CSV file.");
        }

        var stream = new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            BufferSize = 65_536,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        try
        {
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            stream.Position = 0;
            var descriptor = new HistoricalCandleDatasetDescriptor(
                sourceId,
                HistoricalCandleDatasetContract.CsvSchemaVersion,
                Convert.ToHexString(hash),
                instrumentId,
                timeframe);
            HistoricalCandleDatasetContract.ValidateDescriptor(descriptor);
            return new CsvHistoricalCandleDataset(stream, knownAt, descriptor);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public IAsyncEnumerable<Candle> ReadAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _readStarted, 1) != 0)
        {
            throw new InvalidOperationException("Historical candle dataset can only be streamed once.");
        }

        return ReadCoreAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    private async IAsyncEnumerable<Candle> ReadCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            _stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4_096,
            leaveOpen: true);
        var header = await reader.ReadLineAsync(cancellationToken);
        if (!string.Equals(header, HistoricalCandleDatasetContract.CsvHeader, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Historical candle CSV header is invalid.");
        }

        long count = 0;
        Candle? previous = null;
        DateTimeOffset firstOpen = default;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0 || line.Length > MaximumLineLength)
            {
                throw new DomainRuleViolationException("Historical candle CSV line length is invalid.");
            }

            var candle = ParseLine(line, count + 2);
            if (previous is not null && candle.OpenTime != previous.CloseTime)
            {
                throw new DomainRuleViolationException(
                    "Historical candle CSV must be contiguous and strictly ordered.");
            }

            if (count == 0)
            {
                firstOpen = candle.OpenTime;
            }

            previous = candle;
            count = checked(count + 1);
            yield return candle;
        }

        if (count == 0 || previous is null)
        {
            throw new DomainRuleViolationException("Historical candle CSV contains no candles.");
        }

        CompletedSummary = new HistoricalCandleDatasetSummary(
            count,
            firstOpen,
            previous.CloseTime);
    }

    private Candle ParseLine(string line, long lineNumber)
    {
        var remaining = line.AsSpan();
        if (!TryTake(ref remaining, out var openTimeText) ||
            !TryTake(ref remaining, out var openText) ||
            !TryTake(ref remaining, out var highText) ||
            !TryTake(ref remaining, out var lowText) ||
            !TryTake(ref remaining, out var closeText) ||
            remaining.IsEmpty || remaining.Contains(','))
        {
            throw InvalidLine(lineNumber);
        }

        if (!DateTimeOffset.TryParseExact(
                openTimeText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var openTime) ||
            openTime.Offset != TimeSpan.Zero ||
            !TryDecimal(openText, out var open) ||
            !TryDecimal(highText, out var high) ||
            !TryDecimal(lowText, out var low) ||
            !TryDecimal(closeText, out var close) ||
            !TryDecimal(remaining, out var baseVolume))
        {
            throw InvalidLine(lineNumber);
        }

        try
        {
            return Candle.CreateClosed(
                Descriptor.InstrumentId,
                Descriptor.Timeframe,
                openTime,
                _knownAt,
                open,
                high,
                low,
                close,
                baseVolume);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidLine(lineNumber);
        }
    }

    private static bool TryTake(
        ref ReadOnlySpan<char> remaining,
        out ReadOnlySpan<char> field)
    {
        var separator = remaining.IndexOf(',');
        if (separator <= 0)
        {
            field = default;
            return false;
        }

        field = remaining[..separator];
        remaining = remaining[(separator + 1)..];
        return true;
    }

    private static bool TryDecimal(ReadOnlySpan<char> value, out decimal parsed) =>
        decimal.TryParse(
            value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out parsed);

    private static DomainRuleViolationException InvalidLine(long lineNumber) =>
        new($"Historical candle CSV line {lineNumber} is invalid.");
}
