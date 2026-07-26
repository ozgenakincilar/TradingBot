using System.Runtime.CompilerServices;
using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public sealed record HistoricalCandleExportRequest(
    InstrumentId InstrumentId,
    Timeframe Timeframe,
    DateTimeOffset FromInclusive,
    DateTimeOffset ToExclusive,
    string SourceId,
    string OutputPath);

public sealed record HistoricalCandleExportArtifact(
    string FilePath,
    DateTimeOffset ExportedAt,
    HistoricalCandleDatasetDescriptor Descriptor,
    HistoricalCandleDatasetSummary Summary);

public interface IHistoricalCandleDatasetSink
{
    ValueTask<HistoricalCandleExportArtifact> WriteAsync(
        HistoricalCandleExportRequest request,
        DateTimeOffset exportedAt,
        IAsyncEnumerable<Candle> candles,
        CancellationToken cancellationToken);
}

public sealed class ExportHistoricalCandleDataset
{
    private const int MaximumPageSize = 100;
    private static readonly TimeSpan MinimumPageInterval = TimeSpan.FromMilliseconds(100);
    private readonly IClosedCandleHistoryClient _historyClient;
    private readonly IHistoricalCandleDatasetSink _sink;
    private readonly TimeProvider _timeProvider;

    public ExportHistoricalCandleDataset(
        IClosedCandleHistoryClient historyClient,
        IHistoricalCandleDatasetSink sink,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(historyClient);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _historyClient = historyClient;
        _sink = sink;
        _timeProvider = timeProvider;
    }

    public async ValueTask<HistoricalCandleExportArtifact> ExecuteAsync(
        HistoricalCandleExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedCount = ValidateAndCount(request);
        var exportedAt = _timeProvider.GetUtcNow();
        if (exportedAt.Offset != TimeSpan.Zero || exportedAt < request.ToExclusive)
        {
            throw new DomainRuleViolationException(
                "Historical candle export time must be UTC and cover the closed range.");
        }

        var artifact = await _sink.WriteAsync(
            request,
            exportedAt,
            ReadPagesAsync(request, expectedCount, cancellationToken),
            cancellationToken);
        ValidateArtifact(request, exportedAt, expectedCount, artifact);
        return artifact;
    }

    private async IAsyncEnumerable<Candle> ReadPagesAsync(
        HistoricalCandleExportRequest request,
        long expectedCount,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = request.FromInclusive;
        var remaining = expectedCount;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageCount = (int)Math.Min(MaximumPageSize, remaining);
            var pageEnd = AddCandles(cursor, request.Timeframe, pageCount);
            var page = await _historyClient.GetAsync(
                request.InstrumentId,
                request.Timeframe,
                cursor,
                pageEnd,
                cancellationToken);
            if (page.Count != pageCount)
            {
                throw new DomainRuleViolationException(
                    "Historical candle page did not cover its requested range.");
            }

            foreach (var candle in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candle.InstrumentId != request.InstrumentId ||
                    candle.Timeframe != request.Timeframe ||
                    candle.OpenTime != cursor)
                {
                    throw new DomainRuleViolationException(
                        "Historical candle export page is not contiguous or has an invalid identity.");
                }

                yield return candle;
                cursor = candle.CloseTime;
                remaining--;
            }

            if (remaining > 0)
            {
                await Task.Delay(MinimumPageInterval, _timeProvider, cancellationToken);
            }
        }

        if (cursor != request.ToExclusive)
        {
            throw new DomainRuleViolationException(
                "Historical candle export did not reach the requested end boundary.");
        }
    }

    private static long ValidateAndCount(HistoricalCandleExportRequest request)
    {
        if (request.InstrumentId == default || request.Timeframe == default ||
            string.IsNullOrWhiteSpace(request.OutputPath) ||
            request.FromInclusive == default || request.FromInclusive.Offset != TimeSpan.Zero ||
            request.ToExclusive == default || request.ToExclusive.Offset != TimeSpan.Zero ||
            request.ToExclusive <= request.FromInclusive ||
            !request.Timeframe.IsBoundary(request.FromInclusive) ||
            !request.Timeframe.IsBoundary(request.ToExclusive))
        {
            throw new DomainRuleViolationException("Historical candle export request is invalid.");
        }

        HistoricalCandleDatasetContract.ValidateDescriptor(new HistoricalCandleDatasetDescriptor(
            request.SourceId,
            HistoricalCandleDatasetContract.CsvSchemaVersion,
            new string('0', 64),
            request.InstrumentId,
            request.Timeframe));
        var durationTicks = request.ToExclusive.Ticks - request.FromInclusive.Ticks;
        if (durationTicks % request.Timeframe.Duration.Ticks != 0)
        {
            throw new DomainRuleViolationException(
                "Historical candle export range must contain complete timeframe intervals.");
        }

        return durationTicks / request.Timeframe.Duration.Ticks;
    }

    private static DateTimeOffset AddCandles(
        DateTimeOffset start,
        Timeframe timeframe,
        int count)
    {
        try
        {
            return start.AddTicks(checked(timeframe.Duration.Ticks * count));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "Historical candle export page exceeded the supported time range.");
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Historical candle export page exceeded the supported time range.");
        }
    }

    private static void ValidateArtifact(
        HistoricalCandleExportRequest request,
        DateTimeOffset exportedAt,
        long expectedCount,
        HistoricalCandleExportArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        HistoricalCandleDatasetContract.ValidateDescriptor(artifact.Descriptor);
        if (string.IsNullOrWhiteSpace(artifact.FilePath) ||
            artifact.ExportedAt != exportedAt ||
            artifact.Descriptor.InstrumentId != request.InstrumentId ||
            artifact.Descriptor.Timeframe != request.Timeframe ||
            !string.Equals(artifact.Descriptor.SourceId, request.SourceId, StringComparison.Ordinal) ||
            artifact.Summary.CandleCount != expectedCount ||
            artifact.Summary.FirstOpenTime != request.FromInclusive ||
            artifact.Summary.LastCloseTime != request.ToExclusive)
        {
            throw new DomainRuleViolationException(
                "Historical candle export artifact does not match the request.");
        }
    }
}
