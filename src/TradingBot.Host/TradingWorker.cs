using Microsoft.Extensions.Options;
using TradingBot.Application;
using TradingBot.Application.Execution;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Host;

public sealed class TradingWorker(
    MarketSnapshotService snapshots,
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    ILogger<TradingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var instrumentId = InstrumentId.Create(settings.Exchange, settings.Symbol);
        var policy = new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(settings.MinimumFillLatencyMilliseconds),
            Percentage.FromPercent(settings.CommissionPercent),
            settings.SlippageBasisPoints,
            Percentage.FromPercent(settings.MaximumLiquidityParticipationPercent));
        policy.Validate();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var read = await snapshots.GetAsync(
                    instrumentId,
                    TimeSpan.FromSeconds(settings.MaximumMarketDataAgeSeconds),
                    stoppingToken);
                if (read.MarketEvent is null)
                {
                    logger.LogWarning(
                        "Market event withheld for {Instrument}: {IntegrityStatus}, fresh={IsFresh}",
                        instrumentId,
                        read.IntegrityStatus,
                        read.IsFresh);
                }
                else
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<ProcessPaperMarketEvent>();
                    var outcome = await processor.HandleAsync(
                        new ProcessPaperMarketEventCommand(
                            read.MarketEvent,
                            policy,
                            CreateCorrelationId(read.MarketEvent.EventId)),
                        stoppingToken);
                    logger.LogInformation(
                        "Paper market event {MarketEventId} processed {OrderCount} active orders for {Instrument}",
                        read.MarketEvent.EventId,
                        outcome.Orders.Count,
                        instrumentId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Paper trading cycle failed for {Instrument} with {ErrorType}; next bounded cycle will retry",
                    instrumentId,
                    exception.GetType().Name);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(settings.PollingIntervalSeconds),
                stoppingToken);
        }
    }

    private static string CreateCorrelationId(string eventId)
    {
        var suffix = eventId.Length <= 48 ? eventId : eventId[^48..];
        return $"paper-{suffix}";
    }
}
