using System.Runtime.CompilerServices;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Tests;

public sealed class BuyAndHoldBenchmarkTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));

    [Fact]
    public async Task FlatMarketLosesOnlyThroughTwoSidedCosts()
    {
        var report = await RunAsync([100m, 100m, 100m]);

        Assert.True(report.NetReturnPercent < 0m);
        Assert.True(report.GrossReturnPercent > report.NetReturnPercent);
        Assert.True(report.TotalFees > 0m);
        Assert.True(report.EstimatedSpreadCost > 0m);
        Assert.True(report.EstimatedSlippageCost > 0m);
        Assert.Equal(3, report.CandleCount);
        Assert.Equal(Start.AddMinutes(30), report.EntryAt);
        Assert.Equal(Start.AddMinutes(75), report.ExitAt);
    }

    [Fact]
    public async Task RisingMarketProducesPositiveReturnAndStableResult()
    {
        decimal[] closes = [100m, 110m, 120m];

        var first = await RunAsync(closes);
        var second = await RunAsync(closes);

        Assert.Equal(first, second);
        Assert.True(first.NetReturnPercent > 0m);
        Assert.InRange(first.MaximumDrawdownPercent, 0m, 1m);
    }

    [Fact]
    public async Task FallingMarketRecordsDrawdown()
    {
        var report = await RunAsync([100m, 90m, 80m]);

        Assert.True(report.NetReturnPercent < 0m);
        Assert.True(report.MaximumDrawdownPercent > 0m);
    }

    [Fact]
    public async Task MissingOosBoundaryIsRejected()
    {
        var split = Split();
        var candles = Candles([100m, 100m]).Skip(1).ToArray();

        var action = () => new BuyAndHoldBenchmark().RunAsync(
            Stream(candles), split, Policy(), Instrument, Signal, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task InstrumentRulesQuantizeBenchmarkEntryAndExit()
    {
        var policy = Policy() with
        {
            InstrumentRules = TradingBot.Domain.Instruments.Instrument.Create(
                Instrument,
                priceTickSize: 1m,
                quantityStepSize: 0.1m,
                minimumQuantity: 0.1m,
                minimumNotional: 10m)
        };

        var report = await new BuyAndHoldBenchmark().RunAsync(
            Stream(Candles([100m, 110m, 120m])),
            Split(),
            policy,
            Instrument,
            Signal,
            CancellationToken.None);

        Assert.Equal(0.9m, report.BaseQuantity);
        Assert.Equal(102m, report.EntryPrice);
        Assert.Equal(118m, report.ExitPrice);
        Assert.True(report.EndingCashBalance > 900m);
        Assert.Equal(0m, report.BaseQuantity % 0.1m);
        Assert.Equal(0m, report.EntryPrice % 1m);
        Assert.Equal(0m, report.ExitPrice % 1m);
    }

    [Fact]
    public async Task UntradableBenchmarkLiquidationIsRejected()
    {
        var policy = Policy() with
        {
            InstrumentRules = TradingBot.Domain.Instruments.Instrument.Create(
                Instrument,
                priceTickSize: 0.1m,
                quantityStepSize: 0.1m,
                minimumQuantity: 0.1m,
                minimumNotional: 10m)
        };

        var action = () => new BuyAndHoldBenchmark().RunAsync(
            Stream(Candles([100m, 1m, 1m])),
            Split(),
            policy,
            Instrument,
            Signal,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task TemporaryBelowMinimumMarkDoesNotRejectTradableFinalLiquidation()
    {
        var policy = Policy() with
        {
            InstrumentRules = TradingBot.Domain.Instruments.Instrument.Create(
                Instrument,
                priceTickSize: 0.1m,
                quantityStepSize: 0.1m,
                minimumQuantity: 0.1m,
                minimumNotional: 10m)
        };

        var report = await new BuyAndHoldBenchmark().RunAsync(
            Stream(Candles([100m, 1m, 120m])),
            Split(),
            policy,
            Instrument,
            Signal,
            CancellationToken.None);

        Assert.True(report.NetLiquidationValue > 0m);
        Assert.True(report.MaximumDrawdownPercent > 0m);
    }

    [Fact]
    public async Task DynamicExecutionFailsClosedUntilBenchmarkCostParityExists()
    {
        var policy = Policy() with
        {
            DynamicExecution = new VolatilityAdjustedExecutionPolicy(
                2m, 100m, 1m, 150m, 1m, 2m, 5m, 20m, 4)
        };

        var action = () => new BuyAndHoldBenchmark().RunAsync(
            Stream(Candles([100m, 110m, 120m])),
            Split(),
            policy,
            Instrument,
            Signal,
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Contains("benchmark cost parity", exception.Message, StringComparison.Ordinal);
    }

    private static Task<BuyAndHoldBenchmarkReport> RunAsync(decimal[] closes) =>
        new BuyAndHoldBenchmark().RunAsync(
            Stream(Candles(closes)),
            Split(),
            Policy(),
            Instrument,
            Signal,
            CancellationToken.None);

    private static ChronologicalDatasetSplit Split() => ChronologicalDatasetSplit.Create(
        Start,
        Start.AddMinutes(15),
        Start.AddMinutes(30),
        Start.AddMinutes(75));

    private static IReadOnlyList<Candle> Candles(decimal[] closes) => closes
        .Select((close, index) => Candle.CreateClosed(
            Instrument,
            Signal,
            Start.AddMinutes(30 + (15 * index)),
            Start.AddMinutes(45 + (15 * index)),
            index == 0 ? 100m : closes[index - 1],
            Math.Max(index == 0 ? 100m : closes[index - 1], close),
            Math.Min(index == 0 ? 100m : closes[index - 1], close),
            close,
            baseVolume: 10m))
        .ToArray();

    private static async IAsyncEnumerable<Candle> Stream(
        IEnumerable<Candle> candles,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var candle in candles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candle;
            await Task.Yield();
        }
    }

    private static BacktestExecutionPolicy Policy() => new(
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
}
