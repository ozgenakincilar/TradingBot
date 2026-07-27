using System.Net;

namespace TradingBot.Host.Tests;

public sealed class ForwardEvidenceTelemetryStateTests
{
    [Fact]
    public void AtomicSnapshotTracksCycleDiskAndLastSealedWindow()
    {
        var state = new ForwardEvidenceTelemetryState();
        var completedAt = new DateTimeOffset(2026, 8, 27, 0, 0, 1, TimeSpan.Zero);

        state.RecordSuccessfulCycle(
            completedAt,
            completedWindowCount: 1,
            sealedWindowCount: 1,
            windowSealed: true,
            diskAvailableBytes: 1_000_000);

        var snapshot = state.Snapshot;
        Assert.True(snapshot.IsHealthy);
        Assert.Equal(completedAt, snapshot.LastSuccessfulCycleAt);
        Assert.Equal(1, snapshot.CompletedWindowCount);
        Assert.Equal(1, snapshot.SealedWindowCount);
        Assert.Equal(0, snapshot.LastSealedWindowIndex);
        Assert.Equal(1_000_000, snapshot.DiskAvailableBytes);
    }

    [Fact]
    public void ConcurrentCountersDoNotLoseRetryOrSqlFailures()
    {
        var state = new ForwardEvidenceTelemetryState();

        Parallel.For(0, 10_000, _ =>
        {
            state.RecordHttpRetry();
            state.RecordSqlError();
        });

        Assert.Equal(10_000, state.Snapshot.HttpRetryCount);
        Assert.Equal(10_000, state.Snapshot.SqlErrorCount);
    }

    [Fact]
    public void HotStateUpdatesAndSnapshotsAllocateNoManagedMemory()
    {
        var state = new ForwardEvidenceTelemetryState();
        var completedAt = new DateTimeOffset(2026, 8, 27, 0, 0, 1, TimeSpan.Zero);
        state.RecordSuccessfulCycle(completedAt, 1, 1, true, 1_000_000);
        _ = state.Snapshot;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            state.RecordHttpRetry();
            state.RecordSqlError();
            state.RecordSuccessfulCycle(completedAt, 1, 1, false, 1_000_000);
            _ = state.Snapshot;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public async Task RetryableHttpStatusIncrementsCounter()
    {
        var state = new ForwardEvidenceTelemetryState();
        using var handler = new ForwardEvidenceHttpTelemetryHandler(state)
        {
            InnerHandler = new StatusHandler(HttpStatusCode.TooManyRequests)
        };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://tr.okx.com/test");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, state.Snapshot.HttpRetryCount);
    }

    [Fact]
    public void StorageLeaseRejectsSecondWriterUntilOwnerIsReleased()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tradingbot-lease-{Guid.NewGuid():N}");
        try
        {
            using (ForwardEvidenceSingleInstanceLease.Acquire(root))
            {
                Assert.Throws<InvalidOperationException>(() =>
                    ForwardEvidenceSingleInstanceLease.Acquire(root));
            }

            using var reopened = ForwardEvidenceSingleInstanceLease.Acquire(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
