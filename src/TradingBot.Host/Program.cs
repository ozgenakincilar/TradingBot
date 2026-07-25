using TradingBot.Application;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Orders;
using TradingBot.Application.Execution;
using TradingBot.Application.Portfolio;
using TradingBot.Domain;
using TradingBot.Host;
using TradingBot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var tradingBotConnectionString = builder.Configuration.GetConnectionString("TradingBot");
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
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Symbol),
        "Trading sembolü zorunludur.")
    .Validate(static options => !string.IsNullOrWhiteSpace(options.Exchange),
        "Trading borsası zorunludur.")
    .Validate(static options => options.PollingIntervalSeconds is >= 1 and <= 300,
        "Polling aralığı 1-300 saniye arasında olmalıdır.")
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
builder.Services.AddSingleton<IMarketDataClient, PaperMarketDataClient>();
builder.Services.AddSingleton<MarketSnapshotService>();
builder.Services.AddScoped<PersistRiskApprovedOrder>();
builder.Services.AddScoped<ApplySpotOrderFill>();
builder.Services.AddScoped<ProcessPaperOrderSnapshot>();
builder.Services.AddScoped<ProcessPaperMarketEvent>();
builder.Services.AddHostedService<TradingWorker>();
builder.Services.AddTradingBotPersistence(tradingBotConnectionString);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    mode = TradingMode.Paper.ToString()
}));

app.Run();
