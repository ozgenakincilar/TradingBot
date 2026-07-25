using Microsoft.Extensions.Options;
using TradingBot.Application.Execution;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Host;

public sealed class OkxTradingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    ILogger<OkxTradingWorker> logger) : BackgroundService
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
        var failures = 0;
        var nextProcessingAt = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var streamScope = scopeFactory.CreateAsyncScope();
                var session = streamScope.ServiceProvider.GetRequiredService<MarketDataStreamSession>();
                await foreach (var marketEvent in session.ReadValidatedAsync(
                                   instrumentId,
                                   TimeSpan.FromSeconds(settings.MaximumMarketDataAgeSeconds),
                                   stoppingToken))
                {
                    failures = 0;
                    if (marketEvent.ReceivedAt < nextProcessingAt)
                    {
                        continue;
                    }

                    nextProcessingAt = marketEvent.ReceivedAt.AddSeconds(
                        settings.PollingIntervalSeconds);
                    await using var eventScope = scopeFactory.CreateAsyncScope();
                    var processor = eventScope.ServiceProvider.GetRequiredService<ProcessPaperMarketEvent>();
                    var outcome = await processor.HandleAsync(
                        new ProcessPaperMarketEventCommand(
                            marketEvent,
                            policy,
                            CreateCorrelationId(marketEvent.EventId)),
                        stoppingToken);
                    logger.LogInformation(
                        "OKX event {MarketEventId} processed {OrderCount} paper orders for {Instrument}",
                        marketEvent.EventId,
                        outcome.Orders.Count,
                        instrumentId);
                }

                throw new IOException("OKX public market stream ended unexpectedly.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failures = Math.Min(failures + 1, 5);
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, failures - 1)) +
                              TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1_001));
                logger.LogError(
                    "OKX stream failed for {Instrument} with {ErrorType}; reconnect in {BackoffMs} ms",
                    instrumentId,
                    exception.GetType().Name,
                    backoff.TotalMilliseconds);
                await Task.Delay(backoff, stoppingToken);
            }
        }
    }

    private static string CreateCorrelationId(string eventId)
    {
        var suffix = eventId.Length <= 48 ? eventId : eventId[^48..];
        return $"paper-{suffix}";
    }
}
