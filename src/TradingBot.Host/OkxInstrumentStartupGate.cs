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

        try
        {
            var timeframe = Timeframe.Create(
                TimeSpan.FromSeconds(settings.CandleTimeframeSeconds));
            var warmup = scope.ServiceProvider.GetRequiredService<WarmUpClosedCandles>();
            var result = await warmup.HandleAsync(
                instrumentId,
                timeframe,
                settings.WarmupCandleCount,
                timeProvider.GetUtcNow(),
                cancellationToken);
            readiness.MarkCandleHistoryReady(
                settings.CandleTimeframeSeconds,
                result.Candles.Count);
            logger.LogInformation(
                "OKX closed-candle warm-up ready for {Instrument}: timeframe={TimeframeSeconds}s, candles={CandleCount}, through={ToExclusive}",
                instrumentId,
                settings.CandleTimeframeSeconds,
                result.Candles.Count,
                result.ToExclusive);
        }
        catch (Exception exception)
        {
            readiness.MarkCandleHistoryNotReady(exception.GetType().Name);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
