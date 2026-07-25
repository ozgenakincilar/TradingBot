using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Tests;

public sealed class EnsureSpotInstrumentTradableTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");

    [Fact]
    public async Task LiveSpotMetadataPassesStartupGate()
    {
        var metadata = Metadata(isTradingEnabled: true, state: "live");

        var result = await new EnsureSpotInstrumentTradable(new StubCatalog(metadata)).HandleAsync(
            Instrument,
            CancellationToken.None);

        Assert.Equal(metadata, result);
    }

    [Fact]
    public async Task SuspendedInstrumentFailsStartupGate()
    {
        var validator = new EnsureSpotInstrumentTradable(
            new StubCatalog(Metadata(isTradingEnabled: false, state: "suspend")));

        var action = () => validator.HandleAsync(Instrument, CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task InvalidTradingFiltersFailStartupGate()
    {
        var invalid = Metadata(isTradingEnabled: true, state: "live") with { PriceTickSize = 0m };
        var validator = new EnsureSpotInstrumentTradable(new StubCatalog(invalid));

        var action = () => validator.HandleAsync(Instrument, CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private static SpotInstrumentMetadata Metadata(bool isTradingEnabled, string state) =>
        new(
            Instrument,
            AssetCode.Create("BTC"),
            AssetCode.Create("USDT"),
            0.1m,
            0.00000001m,
            0.00001m,
            isTradingEnabled,
            state);

    private sealed class StubCatalog(SpotInstrumentMetadata metadata) : ISpotInstrumentCatalog
    {
        public ValueTask<SpotInstrumentMetadata> GetAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(metadata);
    }
}
