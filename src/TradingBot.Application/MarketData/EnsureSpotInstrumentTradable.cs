using TradingBot.Application.Abstractions;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.MarketData;

public sealed class EnsureSpotInstrumentTradable(ISpotInstrumentCatalog catalog)
{
    public async ValueTask<SpotInstrumentMetadata> HandleAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        var metadata = await catalog.GetAsync(instrumentId, cancellationToken);
        if (metadata.InstrumentId != instrumentId ||
            metadata.PriceTickSize <= 0m ||
            metadata.QuantityStepSize <= 0m ||
            metadata.MinimumQuantity <= 0m)
        {
            throw new DomainRuleViolationException("Spot instrument metadata is invalid.");
        }

        if (!metadata.IsTradingEnabled ||
            !string.Equals(metadata.State, "live", StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Spot instrument is not live and tradable.");
        }

        return metadata;
    }
}
