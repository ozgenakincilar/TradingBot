using System.Net;
using System.Text.Json;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
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
        if (arguments.FirstOrDefault() == "export-candles")
        {
            return await ExportAsync(arguments, shutdown.Token);
        }

        if (arguments.FirstOrDefault() == "run-walk-forward")
        {
            return await RunWalkForwardAsync(arguments, shutdown.Token);
        }

        throw new DomainRuleViolationException(
            ResearchExportCommand.Usage + Environment.NewLine + ResearchWalkForwardCommand.Usage);
    }
    catch (OperationCanceledException)
    {
        await Console.Error.WriteLineAsync("Research command was cancelled.");
        return 2;
    }
    catch (Exception exception) when (
        exception is DomainRuleViolationException or HttpRequestException or IOException)
    {
        await Console.Error.WriteLineAsync(
            exception is DomainRuleViolationException
                ? exception.Message
                : "Research command failed at an external I/O boundary.");
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

static async Task<int> ExportAsync(string[] arguments, CancellationToken cancellationToken)
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
    var artifact = await useCase.ExecuteAsync(request, cancellationToken);
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

static async Task<int> RunWalkForwardAsync(
    string[] arguments,
    CancellationToken cancellationToken)
{
    var request = ResearchWalkForwardCommand.Parse(arguments);
    var orchestrator = new WalkForwardBacktestOrchestrator(
        request.DatasetFactory,
        new DeterministicStrategyBacktest(),
        new BacktestExecutionSimulator(),
        new BuyAndHoldBenchmark());
    var report = await orchestrator.RunAsync(
        request.Definition,
        request.ExecutionPolicy,
        request.Schedule,
        request.RandomSeed,
        cancellationToken);
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(report));
    return 0;
}
