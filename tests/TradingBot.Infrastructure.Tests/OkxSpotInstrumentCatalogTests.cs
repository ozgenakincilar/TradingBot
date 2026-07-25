using System.Net;
using System.Text;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxSpotInstrumentCatalogTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");

    [Fact]
    public async Task PublicSpotInstrumentMapsTradingFiltersAndState()
    {
        var handler = new StubHandler(LivePayload());
        var catalog = CreateCatalog(handler);

        var result = await catalog.GetAsync(Instrument, CancellationToken.None);

        Assert.Equal("BTC", result.BaseAsset.Value);
        Assert.Equal("USDT", result.QuoteAsset.Value);
        Assert.Equal(0.1m, result.PriceTickSize);
        Assert.Equal(0.00000001m, result.QuantityStepSize);
        Assert.Equal(0.00001m, result.MinimumQuantity);
        Assert.True(result.IsTradingEnabled);
        Assert.Equal(
            "/api/v5/public/instruments?instType=SPOT&instId=BTC-USDT",
            handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task NonSpotPayloadIsRejected()
    {
        var payload = LivePayload().Replace("\"SPOT\"", "\"MARGIN\"", StringComparison.Ordinal);
        var catalog = CreateCatalog(new StubHandler(payload));

        var action = () => catalog.GetAsync(Instrument, CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task CurrencyFieldsThatConflictWithInstrumentAreRejected()
    {
        var payload = LivePayload().Replace("\"baseCcy\":\"BTC\"", "\"baseCcy\":\"ETH\"", StringComparison.Ordinal);
        var catalog = CreateCatalog(new StubHandler(payload));

        var action = () => catalog.GetAsync(Instrument, CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task SuspendedStateIsMappedAsNotTradingEnabled()
    {
        var payload = LivePayload().Replace("\"live\"", "\"suspend\"", StringComparison.Ordinal);
        var catalog = CreateCatalog(new StubHandler(payload));

        var result = await catalog.GetAsync(Instrument, CancellationToken.None);

        Assert.False(result.IsTradingEnabled);
        Assert.Equal("suspend", result.State);
    }

    private static OkxSpotInstrumentCatalog CreateCatalog(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://tr.okx.test/") });

    private static string LivePayload() => """
        {
          "code":"0",
          "msg":"",
          "data":[{
            "instType":"SPOT",
            "instId":"BTC-USDT",
            "baseCcy":"BTC",
            "quoteCcy":"USDT",
            "tickSz":"0.1",
            "lotSz":"0.00000001",
            "minSz":"0.00001",
            "state":"live"
          }]
        }
        """;

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
