using System.Globalization;
using System.Net;
using System.Text;
using TradingBot.Domain.Common;
using TradingBot.Research;

namespace TradingBot.Research.Tests;

public sealed class ResearchOkxSmokeCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 12, 34, 0, TimeSpan.Zero);

    [Fact]
    public void StrictCommandAcceptsOnlyBoundedDiagnosticArguments()
    {
        var result = ResearchOkxSmokeCommand.Parse(
        [
            "smoke-okx-candles",
            "--instrument", "BTC-USDT",
            "--timeout-seconds", "10"
        ]);

        Assert.Equal("OKX:BTC-USDT", result.InstrumentId.ToString());
        Assert.Equal(TimeSpan.FromSeconds(10), result.Timeout);
    }

    [Theory]
    [InlineData("unknown", "BTC-USDT", "--timeout-seconds", "10")]
    [InlineData("--instrument", "BTCUSDT", "--timeout-seconds", "10")]
    [InlineData("--instrument", "BTC-USDT", "--timeout-seconds", "31")]
    [InlineData("--instrument", "BTC-USDT", "--timeout-seconds", "0")]
    public void UnsafeOrUnboundedCommandIsRejected(
        string optionOne,
        string valueOne,
        string optionTwo,
        string valueTwo)
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            ResearchOkxSmokeCommand.Parse(
            [
                "smoke-okx-candles", optionOne, valueOne, optionTwo, valueTwo
            ]));
    }

    [Fact]
    public async Task ProbeFetchesExactlyTwoClosedCandlesPerTimeframeWithoutPersistence()
    {
        using var observer = new OkxSmokeHttpObserverHandler(new CandleHandler());
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("https://tr.okx.com/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var probe = new OkxCandleSmokeProbe(client, observer, new Clock());

        var result = await probe.RunAsync(
            new ResearchOkxSmokeRequest(
                TradingBot.Domain.Instruments.InstrumentId.Create("OKX", "BTC-USDT"),
                TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        Assert.Equal(2, result.SignalCandleCount);
        Assert.Equal(2, result.TrendCandleCount);
        Assert.Equal(2, result.HttpRequestCount);
        Assert.Equal(0, result.RateLimitResponseCount);
    }

    [Fact]
    public async Task ObserverCountsRateLimitWithoutReadingOrLoggingPayload()
    {
        using var observer = new OkxSmokeHttpObserverHandler(new RateLimitHandler());
        using var client = new HttpClient(observer)
        {
            BaseAddress = new Uri("https://tr.okx.com/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var probe = new OkxCandleSmokeProbe(client, observer, new Clock());

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await probe.RunAsync(
                new ResearchOkxSmokeRequest(
                    TradingBot.Domain.Instruments.InstrumentId.Create("OKX", "BTC-USDT"),
                    TimeSpan.FromSeconds(1)),
                CancellationToken.None));

        Assert.Equal(1, observer.RequestCount);
        Assert.Equal(1, observer.RateLimitResponseCount);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RateLimitHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    }

    private sealed class CandleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isSignal = request.RequestUri!.Query.Contains("bar=15m", StringComparison.Ordinal);
            var duration = isSignal ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(1);
            var end = isSignal
                ? new DateTimeOffset(2026, 7, 27, 12, 30, 0, TimeSpan.Zero)
                : new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);
            var latest = end - duration;
            var earlier = latest - duration;
            var json = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"code\":\"0\",\"data\":[{Row(latest)},{Row(earlier)}]}}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static string Row(DateTimeOffset openTime) => string.Create(
            CultureInfo.InvariantCulture,
            $"[\"{openTime.ToUnixTimeMilliseconds()}\",\"100\",\"101\",\"99\",\"100\",\"10\",\"1000\",\"1000\",\"1\"]");
    }
}
