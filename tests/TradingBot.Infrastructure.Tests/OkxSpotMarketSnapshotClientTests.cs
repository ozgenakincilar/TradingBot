using System.Net;
using System.Text;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxSpotMarketSnapshotClientTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly DateTimeOffset ReceivedAt = new(2026, 7, 26, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidPublicOrderBookMapsToRecoverySnapshot()
    {
        var handler = new StubHandler(SuccessPayload());
        var client = CreateClient(handler);

        var result = await client.GetRecoverySnapshotAsync(Instrument, CancellationToken.None);

        Assert.Equal("okx-rest-BTC-USDT-3235851742", result.EventId);
        Assert.Equal(3235851742, result.Sequence);
        Assert.Equal(ReceivedAt, result.ReceivedAt);
        Assert.Equal(41006.3m, result.Snapshot.BestBid.Value);
        Assert.Equal(0.30178218m, result.Snapshot.BestBidQuantity);
        Assert.Equal(41006.8m, result.Snapshot.BestAsk.Value);
        Assert.Equal(0.60038921m, result.Snapshot.BestAskQuantity);
        Assert.Equal("/api/v5/market/books?instId=BTC-USDT&sz=1", handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task ApiErrorCodeIsRejectedWithoutExposingServerMessage()
    {
        var client = CreateClient(new StubHandler("""
            {"code":"50011","msg":"sensitive upstream detail","data":[]}
            """));

        var action = () => client.GetRecoverySnapshotAsync(Instrument, CancellationToken.None).AsTask();

        var exception = await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Contains("50011", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonOkxSymbolFormatIsRejectedBeforeNetworkCall()
    {
        var handler = new StubHandler(SuccessPayload());
        var client = CreateClient(handler);

        var action = () => client.GetRecoverySnapshotAsync(
            InstrumentId.Create("OKX", "BTCUSDT"),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Null(handler.RequestUri);
    }

    private static OkxSpotMarketSnapshotClient CreateClient(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://tr.okx.test/") },
            new FixedTimeProvider(ReceivedAt));

    private static string SuccessPayload() => """
        {
          "code":"0",
          "msg":"",
          "data":[{
            "asks":[["41006.8","0.60038921","0","1"]],
            "bids":[["41006.3","0.30178218","0","2"]],
            "ts":"1629966436396",
            "seqId":3235851742
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
