using System.Net;

namespace TradingBot.Host;

public readonly record struct ForwardEvidenceTelemetrySnapshot(
    DateTimeOffset? LastSuccessfulCycleAt,
    int CompletedWindowCount,
    int SealedWindowCount,
    int LastSealedWindowIndex,
    long DiskAvailableBytes,
    long HttpRetryCount,
    long SqlErrorCount)
{
    public bool IsHealthy => LastSuccessfulCycleAt is not null && DiskAvailableBytes >= 0;
}

public sealed class ForwardEvidenceTelemetryState
{
    private long _lastSuccessfulCycleUtcTicks;
    private int _completedWindowCount;
    private int _sealedWindowCount;
    private int _lastSealedWindowIndex = -1;
    private long _diskAvailableBytes = -1;
    private long _httpRetryCount;
    private long _sqlErrorCount;

    public ForwardEvidenceTelemetrySnapshot Snapshot
    {
        get
        {
            var ticks = Volatile.Read(ref _lastSuccessfulCycleUtcTicks);
            return new ForwardEvidenceTelemetrySnapshot(
                ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero),
                Volatile.Read(ref _completedWindowCount),
                Volatile.Read(ref _sealedWindowCount),
                Volatile.Read(ref _lastSealedWindowIndex),
                Volatile.Read(ref _diskAvailableBytes),
                Interlocked.Read(ref _httpRetryCount),
                Interlocked.Read(ref _sqlErrorCount));
        }
    }

    public void RecordSuccessfulCycle(
        DateTimeOffset completedAt,
        int completedWindowCount,
        int sealedWindowCount,
        bool windowSealed,
        long diskAvailableBytes)
    {
        if (completedAt.Offset != TimeSpan.Zero || completedWindowCount < 0 ||
            sealedWindowCount < 0 || sealedWindowCount > completedWindowCount ||
            windowSealed && sealedWindowCount == 0 ||
            diskAvailableBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completedWindowCount));
        }

        Volatile.Write(ref _completedWindowCount, completedWindowCount);
        Volatile.Write(ref _sealedWindowCount, sealedWindowCount);
        Volatile.Write(ref _diskAvailableBytes, diskAvailableBytes);
        if (windowSealed)
        {
            Volatile.Write(ref _lastSealedWindowIndex, sealedWindowCount - 1);
        }

        Volatile.Write(ref _lastSuccessfulCycleUtcTicks, completedAt.UtcTicks);
    }

    public void RecordHttpRetry() => Interlocked.Increment(ref _httpRetryCount);

    public void RecordSqlError() => Interlocked.Increment(ref _sqlErrorCount);
}

public sealed class ForwardEvidenceHttpTelemetryHandler(
    ForwardEvidenceTelemetryState telemetry) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500)
            {
                telemetry.RecordHttpRetry();
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            telemetry.RecordHttpRetry();
            throw;
        }
        catch (HttpRequestException)
        {
            telemetry.RecordHttpRetry();
            throw;
        }
    }
}
