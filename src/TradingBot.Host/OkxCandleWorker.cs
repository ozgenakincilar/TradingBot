using Microsoft.Extensions.Options;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Host;

public sealed class OkxCandleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    TradingReadinessState readiness,
    ILogger<OkxCandleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var instrumentId = InstrumentId.Create(settings.Exchange, settings.Symbol);
        var signalTimeframe = Timeframe.Create(
            TimeSpan.FromSeconds(settings.SignalCandleTimeframeSeconds));
        var trendTimeframe = Timeframe.Create(
            TimeSpan.FromSeconds(settings.TrendCandleTimeframeSeconds));
        Timeframe[] timeframes = [signalTimeframe, trendTimeframe];
        var failures = 0;

        readiness.MarkSignalCandleHistoryNotReady("candle-stream-connecting");
        readiness.MarkTrendCandleHistoryNotReady("candle-stream-connecting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var session = scope.ServiceProvider.GetRequiredService<ClosedCandleStreamSession>();
                await foreach (var update in session.ReadValidatedAsync(
                                   instrumentId,
                                   timeframes,
                                   stoppingToken))
                {
                    if (update.Kind == ClosedCandleStreamUpdateKind.SessionReady)
                    {
                        failures = 0;
                        readiness.MarkSignalCandleHistoryReady(
                            settings.SignalCandleTimeframeSeconds,
                            settings.SignalWarmupCandleCount);
                        readiness.MarkTrendCandleHistoryReady(
                            settings.TrendCandleTimeframeSeconds,
                            settings.TrendWarmupCandleCount);
                        logger.LogInformation(
                            "OKX closed-candle stream ready for {Instrument} with {SignalSeconds}s and {TrendSeconds}s timeframes",
                            instrumentId,
                            settings.SignalCandleTimeframeSeconds,
                            settings.TrendCandleTimeframeSeconds);
                        continue;
                    }

                    var candle = update.Candle!;
                    logger.LogInformation(
                        "Validated closed candle received for {Instrument}: timeframe={TimeframeSeconds}s, open={OpenTime}, close={CloseTime}",
                        instrumentId,
                        candle.Timeframe.Duration.TotalSeconds,
                        candle.OpenTime,
                        candle.CloseTime);
                }

                throw new IOException("OKX closed-candle stream ended unexpectedly.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                readiness.MarkSignalCandleHistoryNotReady(exception.GetType().Name);
                readiness.MarkTrendCandleHistoryNotReady(exception.GetType().Name);
                failures = Math.Min(failures + 1, 5);
                var backoff = TimeSpan.FromSeconds(Math.Pow(2, failures - 1)) +
                              TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1_001));
                logger.LogError(
                    "OKX candle stream failed for {Instrument} with {ErrorType}; reconnect in {BackoffMs} ms",
                    instrumentId,
                    exception.GetType().Name,
                    backoff.TotalMilliseconds);
                await Task.Delay(backoff, stoppingToken);
            }
        }
    }
}
