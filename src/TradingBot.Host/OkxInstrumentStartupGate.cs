using Microsoft.Extensions.Options;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Instruments;

namespace TradingBot.Host;

public sealed class OkxInstrumentStartupGate(
    IServiceScopeFactory scopeFactory,
    IOptions<TradingOptions> options,
    TradingReadinessState readiness,
    ILogger<OkxInstrumentStartupGate> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var instrumentId = InstrumentId.Create(settings.Exchange, settings.Symbol);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
