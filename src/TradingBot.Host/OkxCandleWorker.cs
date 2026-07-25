using Microsoft.Extensions.Options;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Host;

public sealed class OkxCandleWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    TimeProvider timeProvider,
    ClosedCandleSeriesStore candleSeries,
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

        await CloseCandleSeriesAsync(
            candleSeries,
            instrumentId,
            signalTimeframe,
            trendTimeframe,
            stoppingToken);
        readiness.MarkSignalCandleHistoryNotReady("candle-stream-connecting");
        readiness.MarkTrendCandleHistoryNotReady("candle-stream-connecting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var session = scope.ServiceProvider.GetRequiredService<ClosedCandleStreamSession>();
                var warmup = scope.ServiceProvider.GetRequiredService<WarmUpClosedCandles>();
                await foreach (var update in session.ReadValidatedAsync(
                                   instrumentId,
                                   timeframes,
                                   stoppingToken))
                {
                    if (update.Kind == ClosedCandleStreamUpdateKind.SessionReady)
                    {
                        var knownAt = timeProvider.GetUtcNow();
                        var signalSeed = await warmup.HandleAsync(
                            instrumentId,
                            signalTimeframe,
                            settings.SignalWarmupCandleCount,
                            knownAt,
                            stoppingToken);
                        var trendSeed = await warmup.HandleAsync(
                            instrumentId,
                            trendTimeframe,
                            settings.TrendWarmupCandleCount,
                            knownAt,
                            stoppingToken);
                        await candleSeries.SeedAsync(signalSeed, stoppingToken);
                        await candleSeries.SeedAsync(trendSeed, stoppingToken);
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
                    var status = await candleSeries.AppendAsync(candle, stoppingToken);
                    if (status is ClosedCandleSeriesUpdateStatus.GapDetected or
                        ClosedCandleSeriesUpdateStatus.Conflicting)
                    {
                        throw new DomainRuleViolationException(
                            "Live closed-candle series lost continuity and requires reseeding.");
                    }

                    logger.LogInformation(
                        "Validated closed candle received for {Instrument}: timeframe={TimeframeSeconds}s, open={OpenTime}, close={CloseTime}, seriesStatus={SeriesStatus}",
                        instrumentId,
                        candle.Timeframe.Duration.TotalSeconds,
                        candle.OpenTime,
                        candle.CloseTime,
                        status);
                }

                throw new IOException("OKX closed-candle stream ended unexpectedly.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await CloseCandleSeriesAsync(
                    candleSeries,
                    instrumentId,
                    signalTimeframe,
                    trendTimeframe,
                    stoppingToken);
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

    private static async ValueTask CloseCandleSeriesAsync(
        ClosedCandleSeriesStore candleSeries,
        InstrumentId instrumentId,
        Timeframe signalTimeframe,
        Timeframe trendTimeframe,
        CancellationToken cancellationToken)
    {
        await candleSeries.MarkNotReadyAsync(instrumentId, signalTimeframe, cancellationToken);
        await candleSeries.MarkNotReadyAsync(instrumentId, trendTimeframe, cancellationToken);
    }
}
