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

    [Fact]
    public async Task InstrumentRulesQuantizePriceAndQuantityConservatively()
    {
        StrategyBacktestDecision[] decisions =
        [
            Item(CandleAt(0, 100m, 100m), StrategyAction.EnterLong,
                StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(CandleAt(1, 100m, 100m), StrategyAction.Hold,
                StrategyPositionState.Long, "long-position-held")
        ];
        var policy = Policy() with
        {
            InstrumentRules = InstrumentRules(
                priceTickSize: 1m,
                quantityStepSize: 0.1m,
                minimumQuantity: 0.1m,
                minimumNotional: 10m)
        };

        var report = await RunAsync(decisions, policy);

        Assert.Equal(1, report.FillCount);
        Assert.Equal(4.8m, report.OpenQuantity);
        Assert.Equal(0m, report.OpenQuantity % 0.1m);
        Assert.Equal(0.4896m, report.TotalFees);
        Assert.False(report.HasPendingExecution);
    }

    [Fact]
    public async Task AllocationBelowInstrumentMinimumIsRejectedWithoutFill()
    {
        var policy = Policy() with
        {
            QuoteAllocation = Percentage.FromPercent(1m),
            InstrumentRules = InstrumentRules(
                priceTickSize: 0.1m,
                quantityStepSize: 0.01m,
                minimumQuantity: 0.01m,
                minimumNotional: 20m)
        };

        var report = await RunAsync(Decisions(110m), policy);

        Assert.Equal(0, report.FillCount);
        Assert.Equal(1_000m, report.NetLiquidationValue);
        Assert.False(report.HasPendingExecution);
    }

    [Fact]
    public async Task MismatchedInstrumentRulesAreRejected()
    {
        var policy = Policy() with
        {
            InstrumentRules = TradingBot.Domain.Instruments.Instrument.Create(
                InstrumentId.Create("OKX", "ETH-USDT"),
                0.01m,
                0.001m,
                0.001m,
                1m)
        };

        var action = () => RunAsync(Decisions(110m), policy);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
    }

    [Fact]
    public async Task UntradableSellRemainderStaysOpenAndPending()
    {
        var policy = Policy() with
        {
            InstrumentRules = InstrumentRules(
                priceTickSize: 0.1m,
                quantityStepSize: 0.1m,
                minimumQuantity: 0.1m,
                minimumNotional: 10m)
        };

        var report = await RunAsync(Decisions(exitOpen: 1m), policy);

        Assert.Equal(1, report.FillCount);
        Assert.True(report.OpenQuantity > 0m);
        Assert.True(report.HasPendingExecution);
        Assert.Equal(0, report.CompletedTradeCount);
    }

    [Fact]
    public async Task DiagnosticsAttributeReasonsCostsAndCompletedCandleExcursions()
    {
        StrategyBacktestDecision[] decisions =
        [
            Item(CandleWithRange(0, 100m, 100m, 100m), StrategyAction.EnterLong,
                StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(CandleWithRange(1, 100m, 110m, 90m), StrategyAction.ExitToFlat,
                StrategyPositionState.Flat, "trend-filter-exit"),
            Item(CandleWithRange(2, 105m, 500m, 1m), StrategyAction.Hold,
                StrategyPositionState.Flat, "no-entry-signal")
        ];
        var simulator = new BacktestExecutionSimulator();

        var report = await simulator.RunWithDiagnosticsAsync(
            Definition(),
            ToAsync(decisions),
            Policy(),
            new BacktestDiagnosticsPolicy(),
            CancellationToken.None);

        var trade = Assert.Single(report.Trades);
        Assert.Equal("signal-ema-cross-up", trade.EntryReasonCode);
        Assert.Equal("trend-filter-exit", trade.ExitReasonCode);
        Assert.Equal(report.Execution.RealizedPnl, trade.NetPnl);
        var reconstructedGross = trade.NetPnl + trade.EstimatedFees +
            trade.EstimatedSpreadCost + trade.EstimatedSlippageCost;
        Assert.InRange(
            Math.Abs(reconstructedGross - trade.GrossPnlBeforeEstimatedCosts),
            0m,
            0.00000000000000000000000001m);
        Assert.True(trade.MaximumFavorableExcursionPercent > 8m);
        Assert.True(trade.MaximumAdverseExcursionPercent > 10m);
        Assert.True(trade.MaximumFavorableExcursionPercent < 11m);
        Assert.True(trade.MaximumAdverseExcursionPercent < 12m);
        Assert.Equal(TimeSpan.FromMinutes(15), trade.HoldingTime);
        Assert.Equal(trade.MaximumFavorableExcursionPercent,
            report.AverageMaximumFavorableExcursionPercent);
        Assert.Equal(trade.MaximumAdverseExcursionPercent,
            report.AverageMaximumAdverseExcursionPercent);
        Assert.Equal(0, report.FavorableExcursionGivenBackTradeCount);
        Assert.Matches("^[0-9A-F]{64}$", report.ReportSha256);
    }

    [Fact]
    public async Task SameInputsProduceSameDiagnosticsHash()
    {
        var simulator = new BacktestExecutionSimulator();

        var first = await simulator.RunWithDiagnosticsAsync(
            Definition(), ToAsync(Decisions(100m)), Policy(),
            new BacktestDiagnosticsPolicy(), CancellationToken.None);
        var second = await simulator.RunWithDiagnosticsAsync(
            Definition(), ToAsync(Decisions(100m)), Policy(),
            new BacktestDiagnosticsPolicy(), CancellationToken.None);

        Assert.Equal(first.Execution, second.Execution);
        Assert.Equal(first.Trades, second.Trades);
        Assert.Equal(first.ReportSha256, second.ReportSha256);
        Assert.Equal(0, first.FavorableExcursionGivenBackTradeCount);
    }

    [Fact]
    public async Task DiagnosticsTradeLimitFailsClosed()
    {
        StrategyBacktestDecision[] decisions =
        [
            Item(CandleAt(0, 100m, 100m), StrategyAction.EnterLong,
                StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(CandleAt(1, 100m, 100m), StrategyAction.ExitToFlat,
                StrategyPositionState.Flat, "trend-filter-exit"),
            Item(CandleAt(2, 100m, 100m), StrategyAction.EnterLong,
                StrategyPositionState.Long, "signal-ema-cross-up"),
            Item(CandleAt(3, 100m, 100m), StrategyAction.ExitToFlat,
                StrategyPositionState.Flat, "trend-filter-exit"),
            Item(CandleAt(4, 100m, 100m), StrategyAction.Hold,
                StrategyPositionState.Flat, "no-entry-signal")
        ];
        var simulator = new BacktestExecutionSimulator();

        var action = () => simulator.RunWithDiagnosticsAsync(
            Definition(), ToAsync(decisions), Policy(),
            new BacktestDiagnosticsPolicy(MaximumCompletedTrades: 1),
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

    private static Candle CandleWithRange(
        int index,
        decimal open,
        decimal high,
        decimal low) =>
        Candle.CreateClosed(
            Instrument,
            Signal,
            Start + (Signal.Duration * index),
            Start.AddHours(2),
            open,
            high,
            low,
            open,
            100m);

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

    private static TradingBot.Domain.Instruments.Instrument InstrumentRules(
        decimal priceTickSize,
        decimal quantityStepSize,
        decimal minimumQuantity,
        decimal minimumNotional) =>
        TradingBot.Domain.Instruments.Instrument.Create(
            Instrument,
            priceTickSize,
            quantityStepSize,
            minimumQuantity,
            minimumNotional);

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
