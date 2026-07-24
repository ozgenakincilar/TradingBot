using TradingBot.Application;
using TradingBot.Application.Abstractions;
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
    .Validate(static options => options.PollingIntervalSeconds is >= 1 and <= 300,
        "Polling aralığı 1-300 saniye arasında olmalıdır.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IMarketDataClient, PaperMarketDataClient>();
builder.Services.AddSingleton<MarketSnapshotService>();
builder.Services.AddHostedService<TradingWorker>();
builder.Services.AddTradingBotPersistence(tradingBotConnectionString);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    mode = TradingMode.Paper.ToString()
}));

app.Run();
