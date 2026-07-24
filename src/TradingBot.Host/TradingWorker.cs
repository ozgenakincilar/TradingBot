using Microsoft.Extensions.Options;
using TradingBot.Application;

namespace TradingBot.Host;

public sealed class TradingWorker(
    MarketSnapshotService snapshots,
    IOptions<TradingOptions> options,
    ILogger<TradingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            var snapshot = await snapshots.GetAsync(settings.Symbol, stoppingToken);
            logger.LogInformation(
                "Market snapshot: {Symbol} {Price} at {Timestamp}",
                snapshot.Symbol,
                snapshot.Price,
                snapshot.Timestamp);

            await Task.Delay(
                TimeSpan.FromSeconds(settings.PollingIntervalSeconds),
                stoppingToken);
        }
    }
}
