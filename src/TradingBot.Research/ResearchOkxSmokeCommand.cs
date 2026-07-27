using System.Diagnostics;
using System.Globalization;
using System.Net;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Integrations.Okx;

namespace TradingBot.Research;

public readonly record struct ResearchOkxSmokeRequest(
    InstrumentId InstrumentId,
    TimeSpan Timeout);

public readonly record struct OkxCandleSmokeResult(
    string Instrument,
    int SignalCandleCount,
    int TrendCandleCount,
    int HttpRequestCount,
    int RateLimitResponseCount,
    long ElapsedMilliseconds);

public static class ResearchOkxSmokeCommand
{
    public static ResearchOkxSmokeRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 5 ||
            !string.Equals(arguments[0], "smoke-okx-candles", StringComparison.Ordinal))
        {
            throw InvalidCommand();
        }

        string? instrument = null;
        int? timeoutSeconds = null;
        for (var index = 1; index < arguments.Count; index += 2)
        {
            var option = arguments[index];
            var value = arguments[index + 1];
            if (option == "--instrument" && instrument is null)
            {
                instrument = value;
            }
            else if (option == "--timeout-seconds" && timeoutSeconds is null &&
                     int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
                         out var parsed))
            {
                timeoutSeconds = parsed;
            }
            else
            {
                throw InvalidCommand();
            }
        }

        if (string.IsNullOrWhiteSpace(instrument) ||
            !instrument.Contains('-', StringComparison.Ordinal) ||
            timeoutSeconds is < 1 or > 30)
        {
            throw InvalidCommand();
        }

        try
        {
            return new ResearchOkxSmokeRequest(
                InstrumentId.Create("OKX", instrument),
                TimeSpan.FromSeconds(timeoutSeconds.GetValueOrDefault()));
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidCommand();
        }
    }

    public static string Usage =>
        "Usage: smoke-okx-candles --instrument BTC-USDT --timeout-seconds <1-30>";

    private static DomainRuleViolationException InvalidCommand() => new(
        "OKX candle smoke command is invalid. " + Usage);
}

public sealed class OkxSmokeHttpObserverHandler(HttpMessageHandler innerHandler) :
    DelegatingHandler(innerHandler)
{
    private int _requestCount;
    private int _rateLimitResponseCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public int RateLimitResponseCount => Volatile.Read(ref _rateLimitResponseCount);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            Interlocked.Increment(ref _rateLimitResponseCount);
        }

        return response;
    }
}

public sealed class OkxCandleSmokeProbe(
    HttpClient httpClient,
    OkxSmokeHttpObserverHandler observer,
    TimeProvider timeProvider)
{
    private static readonly Timeframe Signal =
        Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend =
        Timeframe.Create(TimeSpan.FromHours(1));

    public async ValueTask<OkxCandleSmokeResult> RunAsync(
        ResearchOkxSmokeRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var knownAt = timeProvider.GetUtcNow();
        if (knownAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("OKX smoke knowledge time must be UTC.");
        }

        var history = new OkxClosedCandleHistoryClient(httpClient, timeProvider);
        var started = Stopwatch.GetTimestamp();
        var signalEnd = FloorToBoundary(knownAt, Signal);
        var signal = await history.GetAsync(
            request.InstrumentId,
            Signal,
            signalEnd - (Signal.Duration * 2),
            signalEnd,
            timeout.Token);
        var trendEnd = FloorToBoundary(knownAt, Trend);
        var trend = await history.GetAsync(
            request.InstrumentId,
            Trend,
            trendEnd - (Trend.Duration * 2),
            trendEnd,
            timeout.Token);
        if (signal.Count != 2 || trend.Count != 2)
        {
            throw new DomainRuleViolationException(
                "OKX smoke response did not contain two closed candles per timeframe.");
        }

        return new OkxCandleSmokeResult(
            request.InstrumentId.ToString(),
            signal.Count,
            trend.Count,
            observer.RequestCount,
            observer.RateLimitResponseCount,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static DateTimeOffset FloorToBoundary(
        DateTimeOffset value,
        Timeframe timeframe)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(
            ticks - ticks % timeframe.Duration.Ticks,
            TimeSpan.Zero);
    }
}
