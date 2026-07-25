using TradingBot.Domain.Instruments;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Abstractions;

public sealed record SpotInstrumentMetadata(
    InstrumentId InstrumentId,
    AssetCode BaseAsset,
    AssetCode QuoteAsset,
    decimal PriceTickSize,
    decimal QuantityStepSize,
    decimal MinimumQuantity,
    bool IsTradingEnabled,
    string State);

public interface ISpotInstrumentCatalog
{
    ValueTask<SpotInstrumentMetadata> GetAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken);
}
