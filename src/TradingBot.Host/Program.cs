using Microsoft.Extensions.Options;
using TradingBot.Application;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Orders;
using TradingBot.Application.Execution;
using TradingBot.Application.Portfolio;
using TradingBot.Application.MarketData;
using TradingBot.Domain;
using TradingBot.Host;
using TradingBot.Infrastructure;
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
                                 webSocket.Scheme == "wss"),
        "OKX REST HTTPS ve WebSocket WSS endpoint'leri zorunludur.")
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
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TradingReadinessState>();
builder.Services.AddScoped<PersistRiskApprovedOrder>();
builder.Services.AddScoped<ApplySpotOrderFill>();
builder.Services.AddScoped<ProcessPaperOrderSnapshot>();
builder.Services.AddScoped<ProcessPaperMarketEvent>();
builder.Services.AddTradingBotPersistence(tradingBotConnectionString);

if (marketDataSource == MarketDataSource.OkxPublic)
{
    builder.Services.AddSingleton<OkxBooks5MessageParser>();
    builder.Services.AddHttpClient<OkxSpotMarketSnapshotClient>((serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        client.BaseAddress = new Uri(settings.OkxRestBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddTransient<IMarketDataSnapshotClient>(serviceProvider =>
        serviceProvider.GetRequiredService<OkxSpotMarketSnapshotClient>());
    builder.Services.AddHttpClient<OkxSpotInstrumentCatalog>((serviceProvider, client) =>
    {
        var settings = serviceProvider.GetRequiredService<IOptions<TradingOptions>>().Value;
        client.BaseAddress = new Uri(settings.OkxRestBaseAddress);
        client.Timeout = TimeSpan.FromSeconds(10);
    });
    builder.Services.AddTransient<ISpotInstrumentCatalog>(serviceProvider =>
        serviceProvider.GetRequiredService<OkxSpotInstrumentCatalog>());
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
    builder.Services.AddHostedService<OkxInstrumentStartupGate>();
    builder.Services.AddHostedService<OkxTradingWorker>();
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

app.Run();
