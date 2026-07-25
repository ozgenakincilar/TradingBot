using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Tests;

public sealed class BacktestExecutionSimulatorTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("OKX", "BTC-USDT");
    private static readonly Timeframe Signal = Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe Trend = Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EntryAndExitFillOnFollowingCandleWithAllCosts()
    {
        var report = await RunAsync(Decisions(exitOpen: 110m), Policy());

        Assert.Equal(2, report.FillCount);
        Assert.Equal(1, report.CompletedTradeCount);
        Assert.Equal(1, report.WinningTradeCount);
        Assert.Equal(100m, report.WinRatePercent);
        Assert.True(report.RealizedPnl > 0m);
        Assert.True(report.NetLiquidationValue > report.InitialQuoteBalance);
        Assert.True(report.GrossReturnPercent > report.NetReturnPercent);
        Assert.True(report.GrossProfit > 0m);
        Assert.Equal(0m, report.GrossLoss);
        Assert.True(report.Expectancy > 0m);
        Assert.True(report.TotalFees > 0m);
        Assert.True(report.EstimatedSpreadCost > 0m);
        Assert.True(report.EstimatedSlippageCost > 0m);
        Assert.Equal(Start.AddMinutes(15).AddMilliseconds(100), report.FirstFillAt);
        Assert.Equal(Start.AddMinutes(30).AddMilliseconds(100), report.LastFillAt);
        Assert.Equal(TimeSpan.FromMinutes(15), report.AverageHoldingTime);
        Assert.Equal(0m, report.OpenQuantity);
        Assert.True(report.EndingCashBalance >= 0m);
        Assert.False(report.HasPendingExecution);
    }

    [Fact]
    public async Task FlatMarketProducesNetLossAfterTwoSidedCosts()
    {
        var report = await RunAsync(Decisions(exitOpen: 100m), Policy());

        Assert.True(report.RealizedPnl < 0m);
        Assert.True(report.NetReturnPercent < 0m);
        Assert.Equal(0, report.WinningTradeCount);
        Assert.Equal(0m, report.ProfitFactor);
        Assert.True(report.GrossLoss > 0m);
        Assert.True(report.Expectancy < 0m);
        Assert.True(report.MaximumDrawdownPercent > 0m);
    }

    [Fact]
    public async Task ZeroVolumeDoesNotCreatePhantomFillAndKeepsEntryPending()
    {
        var source = Decisions(exitOpen: 110m, volume: 100m);
        var zeroVolumeEntry = CandleAt(0, 100m, volume: 0m);
        StrategyBacktestDecision[] decisions =
        [
            Item(
                zeroVolumeEntry,
                StrategyAction.EnterLong,
                StrategyPositionState.Long,
                "signal-ema-cross-up"),
            Item(source[1].SignalCandle, StrategyAction.Hold, StrategyPositionState.Long, "long-position-held")
        ];

        var report = await RunAsync(decisions, Policy());

        Assert.Equal(0, report.FillCount);
        Assert.Equal(1_000m, report.NetLiquidationValue);
        Assert.Equal(0m, report.RealizedPnl);
        Assert.True(report.HasPendingExecution);
    }

    [Fact]
    public async Task SameInputsProduceSameExecutionReport()
    {
        var simulator = new BacktestExecutionSimulator();
        var policy = Policy();

        var first = await simulator.RunAsync(
            Definition(),
            ToAsync(Decisions(110m)),
            policy,
            CancellationToken.None);
        var second = await simulator.RunAsync(
            Definition(),
            ToAsync(Decisions(110m)),
            policy,
            CancellationToken.None);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task PartialEntryCarriesRemainingBudgetToLaterCandle()
    {
        StrategyBacktestDecision[] decisions =
        [
            Item(CandleAt(0, 100m, 1m), StrategyAction.EnterLong, StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(CandleAt(1, 100m, 10m), StrategyAction.Hold, StrategyPositionState.Long, "long-position-held"),
            Item(CandleAt(2, 100m, 10m), StrategyAction.Hold, StrategyPositionState.Long, "long-position-held")
        ];

        var report = await RunAsync(decisions, Policy());

        Assert.Equal(2, report.FillCount);
        Assert.True(report.OpenQuantity > 1m);
        Assert.True(report.EndingCashBalance >= 0m);
        Assert.False(report.HasPendingExecution);
    }

    [Fact]
    public async Task LatencyAtLeastOneSignalCandleIsRejected()
    {
        var policy = Policy() with
        {
            PaperExecution = Policy().PaperExecution with
            {
                MinimumLatency = Signal.Duration
            }
        };
        var simulator = new BacktestExecutionSimulator();

        var action = () => simulator.RunAsync(
            Definition(),
            ToAsync(Decisions(110m)),
            policy,
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    private static Task<BacktestExecutionReport> RunAsync(
        IEnumerable<StrategyBacktestDecision> decisions,
        BacktestExecutionPolicy policy) =>
        new BacktestExecutionSimulator().RunAsync(
            Definition(),
            ToAsync(decisions),
            policy,
            CancellationToken.None);

    private static IReadOnlyList<StrategyBacktestDecision> Decisions(
        decimal exitOpen,
        decimal volume = 100m)
    {
        var first = CandleAt(0, 100m, volume);
        var second = CandleAt(1, 100m, volume);
        var third = CandleAt(2, exitOpen, volume);
        return
        [
            Item(first, StrategyAction.EnterLong, StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(second, StrategyAction.ExitToFlat, StrategyPositionState.Flat, "trend-filter-exit"),
            Item(third, StrategyAction.Hold, StrategyPositionState.Flat, "no-entry-signal")
        ];
    }

    private static StrategyBacktestDecision Item(
        Candle signal,
        StrategyAction action,
        StrategyPositionState position,
        string reason)
    {
        var trend = Candle.CreateClosed(
            Instrument,
            Trend,
            Start.AddHours(-1),
            signal.CloseTime,
            100m,
            100m,
            100m,
            100m,
            1m);
        var decision = StrategyDecision.Create(
            Definition(),
            action,
            signal,
            trend,
            signal.CloseTime,
            reason);
        return new StrategyBacktestDecision(decision, position, signal);
    }

    private static Candle CandleAt(int index, decimal open, decimal volume) =>
        Candle.CreateClosed(
            Instrument,
            Signal,
            Start + (Signal.Duration * index),
            Start.AddHours(2),
            open,
            open,
            open,
            open,
            volume);

    private static async IAsyncEnumerable<StrategyBacktestDecision> ToAsync(
        IEnumerable<StrategyBacktestDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            yield return decision;
        }

        await Task.CompletedTask;
    }

    private static BacktestExecutionPolicy Policy() => new(
        InitialQuoteBalance: 1_000m,
        AssetCode.Create("BTC"),
        AssetCode.Create("USDT"),
        Percentage.FromPercent(50m),
        SyntheticSpreadBasisPoints: 20m,
        new PaperExecutionPolicy(
            TimeSpan.FromMilliseconds(100),
            Percentage.FromPercent(0.1m),
            SlippageBasisPoints: 10m,
            Percentage.FromPercent(100m)));

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
}
