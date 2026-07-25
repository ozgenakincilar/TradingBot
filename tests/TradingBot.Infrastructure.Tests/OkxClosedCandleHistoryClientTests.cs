using System.Net;
using System.Text;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Infrastructure.Tests;

public sealed class OkxClosedCandleHistoryClientTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe OneMinute = Timeframe.Create(TimeSpan.FromMinutes(1));
    private static readonly DateTimeOffset Start =
        new(2026, 7, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset KnownAt = Start.AddMinutes(10);

    [Fact]
    public async Task ReverseChronologicalPayloadMapsToRequestedClosedRange()
    {
        var handler = new StubHandler(SuccessPayload());
        var client = CreateClient(handler);

        var result = await client.GetAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(2),
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(Start, result[0].OpenTime);
        Assert.Equal(Start.AddMinutes(1), result[1].OpenTime);
        Assert.Equal(100m, result[0].Open);
        Assert.Equal(12m, result[0].BaseVolume);
        Assert.Equal(
            $"/api/v5/market/history-candles?instId=BTC-USDT&bar=1m&after={Start.AddMinutes(2).ToUnixTimeMilliseconds()}&limit=2",
            handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task IncompleteCandleIsRejected()
    {
        var payload = SuccessPayload().Replace("\"1\"]", "\"0\"]", StringComparison.Ordinal);
        var client = CreateClient(new StubHandler(payload));

        var action = () => client.GetAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(2),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task NonContiguousResponseIsRejected()
    {
        var wrongTimestamp = Start.AddMinutes(3).ToUnixTimeMilliseconds().ToString();
        var payload = SuccessPayload().Replace(
            Start.AddMinutes(1).ToUnixTimeMilliseconds().ToString(),
            wrongTimestamp,
            StringComparison.Ordinal);
        var client = CreateClient(new StubHandler(payload));

        var action = () => client.GetAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(2),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task UnsupportedBarIsRejectedBeforeNetworkCall()
    {
        var handler = new StubHandler(SuccessPayload());
        var client = CreateClient(handler);

        var action = () => client.GetAsync(
            Instrument,
            Timeframe.Create(TimeSpan.FromMinutes(2)),
            Start,
            Start.AddMinutes(2),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task MoreThanOfficialHistoryPageLimitIsRejectedBeforeNetworkCall()
    {
        var handler = new StubHandler(SuccessPayload());
        var client = CreateClient(handler);

        var action = () => client.GetAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(101),
            CancellationToken.None).AsTask();

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task ApiErrorIsSanitized()
    {
        var client = CreateClient(new StubHandler("""
            {"code":"50011","msg":"sensitive upstream detail","data":[]}
            """));

        var action = () => client.GetAsync(
            Instrument,
            OneMinute,
            Start,
            Start.AddMinutes(2),
            CancellationToken.None).AsTask();

        var exception = await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Contains("50011", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OkxClosedCandleHistoryClient CreateClient(StubHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://tr.okx.test/") },
            new FixedTimeProvider(KnownAt));

    private static string SuccessPayload() => $$"""
        {
          "code":"0",
          "msg":"",
          "data":[
            ["{{Start.AddMinutes(1).ToUnixTimeMilliseconds()}}","105","112","98","108","9","0","0","1"],
            ["{{Start.ToUnixTimeMilliseconds()}}","100","110","90","105","12","0","0","1"]
          ]
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
