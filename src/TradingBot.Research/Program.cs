using System.Net;
using System.Text.Json;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Infrastructure.Backtesting;
using TradingBot.Infrastructure.Integrations.Okx;
using TradingBot.Research;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += OnCancel;
    try
    {
        var request = ResearchExportCommand.Parse(arguments);
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://www.okx.com/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        var useCase = new ExportHistoricalCandleDataset(
            new OkxClosedCandleHistoryClient(httpClient, TimeProvider.System),
            new AtomicCsvHistoricalCandleDatasetSink(),
            TimeProvider.System);
        var artifact = await useCase.ExecuteAsync(request, shutdown.Token);
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
        {
            artifact.FilePath,
            artifact.ExportedAt,
            artifact.Descriptor.SourceId,
            artifact.Descriptor.SchemaVersion,
            artifact.Descriptor.Sha256,
            artifact.Summary.CandleCount,
            artifact.Summary.FirstOpenTime,
            artifact.Summary.LastCloseTime
        }));
        return 0;
    }
    catch (OperationCanceledException)
    {
        await Console.Error.WriteLineAsync("Research export was cancelled.");
        return 2;
    }
    catch (Exception exception) when (
        exception is DomainRuleViolationException or HttpRequestException or IOException)
    {
        await Console.Error.WriteLineAsync(
            exception is DomainRuleViolationException
                ? exception.Message
                : "Research export failed at an external I/O boundary.");
        return 1;
    }
    finally
    {
        Console.CancelKeyPress -= OnCancel;
    }

    void OnCancel(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    }
}
