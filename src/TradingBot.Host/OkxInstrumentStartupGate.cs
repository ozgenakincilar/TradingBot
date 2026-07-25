using Microsoft.Extensions.Options;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Host;

public sealed class OkxInstrumentStartupGate(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    TimeProvider timeProvider,
    TradingReadinessState readiness,
    ILogger<OkxInstrumentStartupGate> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var instrumentId = InstrumentId.Create(settings.Exchange, settings.Symbol);
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var validator = scope.ServiceProvider.GetRequiredService<EnsureSpotInstrumentTradable>();
            var metadata = await validator.HandleAsync(instrumentId, cancellationToken);
            readiness.MarkInstrumentReady(instrumentId.ToString());
            logger.LogInformation(
                "OKX Spot instrument {Instrument} is live: tick={TickSize}, lot={LotSize}, min={MinimumSize}",
                instrumentId,
                metadata.PriceTickSize,
                metadata.QuantityStepSize,
                metadata.MinimumQuantity);
        }
        catch (Exception exception)
        {
            readiness.MarkInstrumentNotReady(exception.GetType().Name);
            throw;
        }

        var warmup = scope.ServiceProvider.GetRequiredService<WarmUpClosedCandles>();
        var knownAt = timeProvider.GetUtcNow();
        try
        {
            var result = await WarmUpAsync(
                warmup,
                instrumentId,
                settings.SignalCandleTimeframeSeconds,
                settings.SignalWarmupCandleCount,
                knownAt,
                cancellationToken);
            readiness.MarkSignalCandleHistoryReady(
                settings.SignalCandleTimeframeSeconds,
                result.Candles.Count);
            logger.LogInformation(
                "OKX signal candle warm-up ready for {Instrument}: timeframe={TimeframeSeconds}s, candles={CandleCount}, through={ToExclusive}",
                instrumentId,
                settings.SignalCandleTimeframeSeconds,
                result.Candles.Count,
                result.ToExclusive);
        }
        catch (Exception exception)
        {
            readiness.MarkSignalCandleHistoryNotReady(exception.GetType().Name);
            throw;
        }

        try
        {
            var result = await WarmUpAsync(
                warmup,
                instrumentId,
                settings.TrendCandleTimeframeSeconds,
                settings.TrendWarmupCandleCount,
                knownAt,
                cancellationToken);
            readiness.MarkTrendCandleHistoryReady(
                settings.TrendCandleTimeframeSeconds,
                result.Candles.Count);
            logger.LogInformation(
                "OKX trend candle warm-up ready for {Instrument}: timeframe={TimeframeSeconds}s, candles={CandleCount}, through={ToExclusive}",
                instrumentId,
                settings.TrendCandleTimeframeSeconds,
                result.Candles.Count,
                result.ToExclusive);
        }
        catch (Exception exception)
        {
            readiness.MarkTrendCandleHistoryNotReady(exception.GetType().Name);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static ValueTask<ClosedCandleWarmupResult> WarmUpAsync(
        WarmUpClosedCandles warmup,
        InstrumentId instrumentId,
        int timeframeSeconds,
        int warmupCandleCount,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken) =>
        warmup.HandleAsync(
            instrumentId,
            Timeframe.Create(TimeSpan.FromSeconds(timeframeSeconds)),
            warmupCandleCount,
            knownAt,
            cancellationToken);
}
