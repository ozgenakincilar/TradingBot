using Microsoft.Extensions.Options;
using TradingBot.Application;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Orders;
using TradingBot.Application.Execution;
using TradingBot.Application.Portfolio;
using TradingBot.Application.MarketData;
using TradingBot.Domain;
using TradingBot.Host;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.Backtesting;
using TradingBot.Infrastructure.Integrations.Okx;

var builder = WebApplication.CreateBuilder(args);

var tradingBotConnectionString = builder.Configuration.GetConnectionString("TradingBot");
var marketDataSource = builder.Configuration
    .GetSection(TradingOptions.SectionName)
    .GetValue<MarketDataSource>(nameof(TradingOptions.MarketDataSource));
if (string.IsNullOrWhiteSpace(tradingBotConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:TradingBot must be supplied through environment configuration or a secret provider.");
}

builder.Services
    .AddOptions<TradingOptions>()
    .Bind(builder.Configuration.GetSection(TradingOptions.SectionName))
    .Validate(static options => options.Mode == TradingMode.Paper,
        "İlk sürüm yalnızca Paper modunda çalışabilir.")
    .Validate(static options => Enum.IsDefined(options.MarketDataSource),
        "Market data kaynağı geçersiz.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                (string.Equals(options.Exchange, "OKX", StringComparison.Ordinal) &&
                                 options.Symbol.Contains('-', StringComparison.Ordinal)),
        "OKX public market data için exchange=OKX ve BASE-QUOTE sembolü zorunludur.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                (Uri.TryCreate(options.OkxRestBaseAddress, UriKind.Absolute, out var rest) &&
                                 rest.Scheme == Uri.UriSchemeHttps &&
                                 Uri.TryCreate(options.OkxWebSocketEndpoint, UriKind.Absolute, out var webSocket) &&
                                 webSocket.Scheme == "wss" &&
                                 Uri.TryCreate(options.OkxBusinessWebSocketEndpoint, UriKind.Absolute, out var business) &&
                                 business.Scheme == "wss" &&
                                 business.AbsolutePath == "/ws/v5/business"),
        "OKX REST HTTPS, public WSS ve business WSS endpoint'leri zorunludur.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Symbol),
        "Trading sembolü zorunludur.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Exchange),
        "Trading borsası zorunludur.")
    .Validate(static options => options.PollingIntervalSeconds is >= 1 and <= 300,
        "Polling aralığı 1-300 saniye arasında olmalıdır.")
    .Validate(static options => options.MaximumMarketDataAgeSeconds is >= 1 and <= 300,
        "Market data maksimum yaşı 1-300 saniye arasında olmalıdır.")
    .Validate(static options => options.MinimumFillLatencyMilliseconds is >= 1 and <= 60_000,
        "Paper fill gecikmesi 1-60000 milisaniye arasında olmalıdır.")
    .Validate(static options => options.CommissionPercent is >= 0 and <= 5,
        "Paper komisyonu yüzde 0-5 arasında olmalıdır.")
    .Validate(static options => options.SlippageBasisPoints is >= 0 and <= 1_000,
        "Paper slippage 0-1000 baz puan arasında olmalıdır.")
    .Validate(static options => options.MaximumLiquidityParticipationPercent is > 0 and <= 100,
        "Paper likidite katılımı yüzde 0-100 arasında olmalıdır.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                options.SignalCandleTimeframeSeconds == 900,
        "İlk strateji sürümü için signal candle timeframe 15 dakika olmalıdır.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                options.TrendCandleTimeframeSeconds == 3600,
        "İlk strateji sürümü için trend candle timeframe 1 saat olmalıdır.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                options.SignalWarmupCandleCount is >= 200 and <= 300,
        "Signal warm-up candle sayısı EMA200 için 200-300 arasında olmalıdır.")
    .Validate(static options => options.MarketDataSource != MarketDataSource.OkxPublic ||
                                options.TrendWarmupCandleCount is >= 200 and <= 300,
        "Trend warm-up candle sayısı EMA200 için 200-300 arasında olmalıdır.")
    .ValidateOnStart();

builder.Services
    .AddOptions<ForwardEvidenceOptions>()
    .Bind(builder.Configuration.GetSection(ForwardEvidenceOptions.SectionName))
    .Validate(static options => !options.Enabled ||
                                !string.IsNullOrWhiteSpace(options.PipelineId),
        "Forward evidence pipeline kimliği zorunludur.")
    .Validate(static options => !options.Enabled ||
                                !string.IsNullOrWhiteSpace(options.RootPath),
        "Forward evidence veri kökü zorunludur.")
    .Validate(static options => !options.Enabled ||
                                options.StartInclusive.Offset == TimeSpan.Zero &&
                                options.StartInclusive >=
                                AtrHysteresisValidationOrchestrator.EarliestForwardData,
        "Forward evidence başlangıcı kilitli forward tarihten önce olamaz.")
    .Validate(static options => !options.Enabled ||
                                options.PollingIntervalSeconds is >= 30 and <= 3_600,
        "Forward evidence polling aralığı 30-3600 saniye arasında olmalıdır.")
    .Validate(static options => !options.Enabled || options.MinimumNotional > 0m,
        "Forward evidence minimum notional değeri pozitif olmalıdır.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ForwardEvidenceTelemetryState>();
builder.Services.AddTransient<ForwardEvidenceHttpTelemetryHandler>();
builder.Services.AddSingleton(
    new TradingReadinessState(marketDataSource == MarketDataSource.OkxPublic));
builder.Services.AddSingleton(new ClosedCandleSeriesStore(capacityPerSeries: 300));
builder.Services.AddScoped<PersistRiskApprovedOrder>();
builder.Services.AddScoped<ApplySpotOrderFill>();
builder.Services.AddScoped<ProcessPaperOrderSnapshot>();
builder.Services.AddScoped<ProcessPaperMarketEvent>();
builder.Services.AddTradingBotPersistence(tradingBotConnectionString);

if (marketDataSource == MarketDataSource.OkxPublic)
{
    builder.Services.AddSingleton<OkxBooks5MessageParser>();
    builder.Services.AddSingleton<OkxCandleMessageParser>();
    builder.Services.AddHttpClient<OkxSpotMarketSnapshotClient>((serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        client.BaseAddress = new Uri(settings.OkxRestBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddTransient<IMarketDataSnapshotClient>(serviceProvider =>
        serviceProvider.GetRequiredService<OkxSpotMarketSnapshotClient>());
    var instrumentHttpClient = builder.Services.AddHttpClient<OkxSpotInstrumentCatalog>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
            client.BaseAddress = new Uri(settings.OkxRestBaseAddress);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
    instrumentHttpClient.AddStandardResilienceHandler();
    instrumentHttpClient.AddHttpMessageHandler<ForwardEvidenceHttpTelemetryHandler>();
    builder.Services.AddTransient<ISpotInstrumentCatalog>(serviceProvider =>
        serviceProvider.GetRequiredService<OkxSpotInstrumentCatalog>());
    var historyHttpClient = builder.Services.AddHttpClient<OkxClosedCandleHistoryClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
            client.BaseAddress = new Uri(settings.OkxRestBaseAddress);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
    historyHttpClient.AddStandardResilienceHandler();
    historyHttpClient.AddHttpMessageHandler<ForwardEvidenceHttpTelemetryHandler>();
    builder.Services.AddTransient<IClosedCandleHistoryClient>(serviceProvider =>
        new PagedClosedCandleHistoryClient(
            serviceProvider.GetRequiredService<OkxClosedCandleHistoryClient>(),
            maximumPageSize: 100));
    builder.Services.AddTransient(serviceProvider => new WarmUpClosedCandles(
        serviceProvider.GetRequiredService<IClosedCandleHistoryClient>(),
        maximumCandlesPerRequest: 300));
    builder.Services.AddTransient<EnsureSpotInstrumentTradable>();
    builder.Services.AddSingleton<IMarketDataStreamClient>(serviceProvider =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        return new OkxSpotMarketStreamClient(
            new Uri(settings.OkxWebSocketEndpoint),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<OkxBooks5MessageParser>());
    });
    builder.Services.AddTransient(serviceProvider => new MarketDataStreamSession(
        serviceProvider.GetRequiredService<IMarketDataStreamClient>(),
        serviceProvider.GetRequiredService<IMarketDataSnapshotClient>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        MarketDataRecoveryMode.EveryStreamEventIsSnapshot));
    builder.Services.AddSingleton<IClosedCandleStreamClient>(serviceProvider =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        return new OkxClosedCandleStreamClient(
            new Uri(settings.OkxBusinessWebSocketEndpoint),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<OkxCandleMessageParser>());
    });
    builder.Services.AddTransient(serviceProvider => new ClosedCandleStreamSession(
        serviceProvider.GetRequiredService<IClosedCandleStreamClient>(),
        serviceProvider.GetRequiredService<IClosedCandleHistoryClient>(),
        serviceProvider.GetRequiredService<TimeProvider>(),
        maximumCandlesPerRecovery: 300));
    builder.Services.AddHostedService<OkxInstrumentStartupGate>();
    builder.Services.AddHostedService<OkxCandleWorker>();
    builder.Services.AddHostedService<OkxTradingWorker>();

    var forwardEvidence = builder.Configuration
        .GetSection(ForwardEvidenceOptions.SectionName)
        .Get<ForwardEvidenceOptions>() ?? new ForwardEvidenceOptions();
    if (forwardEvidence.Enabled)
    {
        builder.Services.AddScoped<IForwardEvidenceArtifactStore>(serviceProvider =>
            new ImmutableForwardEvidenceArtifactStore(
                forwardEvidence.RootPath,
                serviceProvider.GetRequiredService<IClosedCandleHistoryClient>(),
                serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.AddScoped<IForwardEvidenceEvaluator>(serviceProvider =>
            new LockedV6ForwardEvidenceEvaluator(
                serviceProvider.GetRequiredService<ISpotInstrumentCatalog>(),
                forwardEvidence.RootPath,
                forwardEvidence.MinimumNotional));
        builder.Services.AddScoped<ForwardEvidencePipeline>();
        builder.Services.AddHostedService<ForwardEvidenceWorker>();
    }
}
else
{
    builder.Services.AddSingleton<IMarketDataClient, PaperMarketDataClient>();
    builder.Services.AddSingleton<MarketSnapshotService>();
    builder.Services.AddHostedService<TradingWorker>();
}

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    mode = TradingMode.Paper.ToString()
}));

app.MapGet("/health/ready", (TradingReadinessState readiness) =>
{
    var snapshot = readiness.Snapshot;
    return snapshot.IsReady
        ? Results.Ok(snapshot)
        : Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/health/forward-evidence", (
    ForwardEvidenceTelemetryState telemetry,
    IOptions<ForwardEvidenceOptions> forwardOptions) =>
{
    var snapshot = telemetry.Snapshot;
    return forwardOptions.Value.Enabled && snapshot.IsHealthy
        ? Results.Ok(snapshot)
        : Results.Json(snapshot, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/metrics/forward-evidence", (
    ForwardEvidenceTelemetryState telemetry) => Results.Ok(telemetry.Snapshot));

app.Run();
