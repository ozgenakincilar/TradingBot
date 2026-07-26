using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Repositories;

namespace TradingBot.Infrastructure.Tests;

public sealed class WalkForwardResultPersistenceIntegrationTests
{
    private const string ConnectionVariable = "TRADINGBOT_TEST_DB_CONNECTION";
    private static readonly DateTimeOffset Start =
        new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));

    [Fact]
    public async Task ReportAndWindowAreStoredAtomicallyAndDuplicateIsIdempotent()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var report = CreateReport(Guid.NewGuid().ToString("N"));
        try
        {
            await using (var firstContext = CreateContext(connectionString))
            {
                var handler = new PersistWalkForwardReport(
                    new WalkForwardResultRepository(firstContext),
                    new TradingUnitOfWork(firstContext));
                var status = await handler.HandleAsync(report, Start, CancellationToken.None);
                Assert.Equal(WalkForwardPersistenceStatus.Stored, status);
            }

            await using (var duplicateContext = CreateContext(connectionString))
            {
                var handler = new PersistWalkForwardReport(
                    new WalkForwardResultRepository(duplicateContext),
                    new TradingUnitOfWork(duplicateContext));
                var status = await handler.HandleAsync(report, Start, CancellationToken.None);
                Assert.Equal(WalkForwardPersistenceStatus.AlreadyStored, status);
            }

            await using var verify = CreateContext(connectionString);
            var stored = await verify.WalkForwardRuns
                .AsNoTracking()
                .SingleAsync(run => run.RunSha256 == report.RunSha256);
            var window = await verify.WalkForwardWindowResults
                .AsNoTracking()
                .SingleAsync(result => result.RunSha256 == report.RunSha256);
            Assert.Equal(report.ReportSha256, stored.ReportSha256);
            Assert.Equal(report.ScheduleSha256, stored.ScheduleSha256);
            Assert.Equal(report.Windows.Count, stored.WindowCount);
            Assert.Equal(report.Windows[0].Manifest.ManifestSha256, window.ManifestSha256);
            Assert.Equal(report.Windows[0].Execution.NetReturnPercent, window.NetReturnPercent);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString);
            var entity = await cleanup.WalkForwardRuns
                .SingleOrDefaultAsync(run => run.RunSha256 == report.RunSha256);
            if (entity is not null)
            {
                cleanup.WalkForwardRuns.Remove(entity);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    private static WalkForwardReport CreateReport(string unique)
    {
        var schedule = WalkForwardSchedule.Create(
            Start,
            Start.AddDays(240),
            TimeSpan.FromDays(180),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30),
            WalkForwardTrainingMode.Rolling,
            Signal,
            Trend);
        var window = schedule.Windows[0];
        var manifest = BacktestRunManifestFactory.Create(
            Definition(),
            ExecutionPolicy(),
            Descriptor($"signal-{unique[..12]}", Signal, unique),
            Summary(),
            Descriptor($"trend-{unique[..12]}", Trend, unique),
            Summary(),
            window.Split,
            BacktestExperimentPlan.Create(
                BacktestRunPurpose.FinalOutOfSampleEvaluation,
                BacktestDatasetPartition.OutOfSample),
            randomSeed: 42);
        var execution = new BacktestExecutionReport(
            InitialQuoteBalance: 1_000m,
            EndingCashBalance: 1_010m,
            OpenQuantity: 0m,
            NetLiquidationValue: 1_010m,
            GrossReturnPercent: 1.1m,
            NetReturnPercent: 1m,
            RealizedPnl: 10m,
            GrossProfit: 0m,
            GrossLoss: 0m,
            Expectancy: null,
            TotalFees: 1m,
            EstimatedSpreadCost: 0m,
            EstimatedSlippageCost: 0m,
            MaximumDrawdownPercent: 0m,
            FillCount: 0,
            CompletedTradeCount: 0,
            WinningTradeCount: 0,
            WinRatePercent: null,
            ProfitFactor: null,
            AverageHoldingTime: null,
            HasPendingExecution: false,
            FirstFillAt: null,
            LastFillAt: null);
        return WalkForwardReportFactory.Create(
            schedule,
            [new WalkForwardWindowResult(
                window.Index,
                manifest,
                execution,
                new BuyAndHoldBenchmarkReport(
                    InitialQuoteBalance: 1_000m,
                    AllocatedQuoteBalance: 100m,
                    EndingCashBalance: 900m,
                    BaseQuantity: 1m,
                    EntryPrice: 100m,
                    ExitPrice: 110m,
                    NetLiquidationValue: 1_010m,
                    GrossReturnPercent: 1m,
                    NetReturnPercent: 1m,
                    TotalFees: 0m,
                    EstimatedSpreadCost: 0m,
                    EstimatedSlippageCost: 0m,
                    MaximumDrawdownPercent: 0m,
                    CandleCount: 2_880,
                    EntryAt: window.Split.ValidationEndExclusive,
                    ExitAt: window.Split.OutOfSampleEndExclusive))]);
    }

    private static HistoricalCandleDatasetDescriptor Descriptor(
        string sourceId,
        Timeframe timeframe,
        string hashSeed)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{hashSeed}-{timeframe.Duration.Ticks}")));
        return new HistoricalCandleDatasetDescriptor(
            sourceId,
            HistoricalCandleDatasetContract.CsvSchemaVersion,
            hash,
            Instrument,
            timeframe);
    }

    private static HistoricalCandleDatasetSummary Summary() =>
        new(CandleCount: 23_040, Start, Start.AddDays(240));

    private static StrategyDefinition Definition() => StrategyDefinition.Create(
        "btc-usdt-long-flat-baseline",
        1,
        Instrument,
        Signal,
        Trend,
        signalEmaPeriod: 20,
        trendEmaPeriod: 200,
        maximumSignalCandleMovePercent: 2m,
        minimumSignalWarmupCandles: 200,
        minimumTrendWarmupCandles: 200);

    private static BacktestExecutionPolicy ExecutionPolicy() => new(
        InitialQuoteBalance: 1_000m,
        AssetCode.Create("BTC"),
        AssetCode.Create("USDT"),
        Percentage.FromPercent(10m),
        SyntheticSpreadBasisPoints: 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100),
            Percentage.FromPercent(0.1m),
            SlippageBasisPoints: 10m,
            Percentage.FromPercent(5m)));

    private static TradingBotDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TradingBotDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new TradingBotDbContext(options);
    }
}
