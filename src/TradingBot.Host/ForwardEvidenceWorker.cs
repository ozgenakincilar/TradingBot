using Microsoft.Extensions.Options;
using System.Data.Common;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Host;

public sealed class ForwardEvidenceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ForwardEvidenceOptions> options,
    IOptions<TradingOptions> tradingOptions,
    TimeProvider timeProvider,
    ForwardEvidenceTelemetryState telemetry,
    ILogger<ForwardEvidenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var instanceLease = ForwardEvidenceSingleInstanceLease.Acquire(
            settings.RootPath);
        var trading = tradingOptions.Value;
        var policy = new ForwardEvidencePolicy(
            settings.PipelineId,
            InstrumentId.Create(trading.Exchange, trading.Symbol),
            Timeframe.Create(TimeSpan.FromMinutes(15)),
            Timeframe.Create(TimeSpan.FromHours(1)),
            settings.StartInclusive);
        policy.Validate();
        var pollingInterval = TimeSpan.FromSeconds(settings.PollingIntervalSeconds);
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var pipeline = scope.ServiceProvider
                    .GetRequiredService<ForwardEvidencePipeline>();
                var result = await pipeline.RunOnceAsync(policy, stoppingToken);
                failures = 0;
                telemetry.RecordSuccessfulCycle(
                    timeProvider.GetUtcNow(),
                    result.CompletedWindowCount,
                    result.SealedWindowCount,
                    result.WindowSealed,
                    GetAvailableDiskBytes(settings.RootPath));
                logger.LogInformation(
                    "Forward evidence cycle completed: completed={CompletedWindowCount}, sealed={SealedWindowCount}, newWindow={WindowSealed}, evaluationStored={EvaluationStored}, accepted={Accepted}",
                    result.CompletedWindowCount,
                    result.SealedWindowCount,
                    result.WindowSealed,
                    result.EvaluationStored,
                    result.IsAccepted);
                await Task.Delay(pollingInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (ContainsDatabaseException(exception))
                {
                    telemetry.RecordSqlError();
                }

                failures = Math.Min(failures + 1, 6);
                var backoff = TimeSpan.FromSeconds(1 << (failures - 1)) +
                              TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1_001));
                logger.LogError(
                    "Forward evidence cycle failed with {ErrorType}; retry in {BackoffMs} ms",
                    exception.GetType().Name,
                    backoff.TotalMilliseconds);
                await Task.Delay(backoff, timeProvider, stoppingToken);
            }
        }
    }

    private static long GetAvailableDiskBytes(string rootPath)
    {
        var fullPath = Path.GetFullPath(rootPath);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException(
                "Forward evidence storage root has no filesystem volume.");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    private static bool ContainsDatabaseException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException)
            {
                return true;
            }
        }

        return false;
    }
}
