using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record LockedAtrHysteresisV6Configuration(
    StrategyDefinition Baseline,
    StrategyDefinition Candidate,
    BacktestExecutionPolicy ExecutionPolicy,
    AtrHysteresisParameterGrid ParameterGrid)
{
    public const int RandomSeed = 1729;

    public static LockedAtrHysteresisV6Configuration Create(Instrument instrumentRules)
    {
        ArgumentNullException.ThrowIfNull(instrumentRules);
        var instrument = instrumentRules.Id;
        if (!string.Equals(instrument.Exchange, "OKX", StringComparison.Ordinal) ||
            !string.Equals(instrument.Symbol, "BTC-USDT", StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                "Locked ATR hysteresis v6 evidence requires OKX BTC-USDT.");
        }

        var signal = Timeframe.Create(TimeSpan.FromMinutes(15));
        var trend = Timeframe.Create(TimeSpan.FromHours(1));
        var baseline = StrategyDefinition.Create(
            "btc-usdt-long-flat-baseline",
            5,
            instrument,
            signal,
            trend,
            signalEmaPeriod: 20,
            trendEmaPeriod: 200,
            maximumSignalCandleMovePercent: 2m,
            minimumSignalWarmupCandles: 200,
            minimumTrendWarmupCandles: 200,
            signalEmaHysteresisBasisPoints: 30m,
            trendStrengthPeriod: 14,
            minimumTrendStrength: 25m,
            requirePositiveDirectionalMovement: true);
        var candidate = StrategyDefinition.Create(
            baseline.StrategyId,
            6,
            instrument,
            signal,
            trend,
            baseline.SignalEmaPeriod,
            baseline.TrendEmaPeriod,
            baseline.MaximumSignalCandleMovePercent,
            baseline.MinimumSignalWarmupCandles,
            baseline.MinimumTrendWarmupCandles,
            signalEmaHysteresisBasisPoints: 0m,
            trendStrengthPeriod: 14,
            minimumTrendStrength: 25m,
            requirePositiveDirectionalMovement: true,
            signalAtrPeriod: 14,
            signalAtrHysteresisMultiplier: 0.2m);
        var execution = new BacktestExecutionPolicy(
            InitialQuoteBalance: 1_000m,
            AssetCode.Create("BTC"),
            AssetCode.Create("USDT"),
            Percentage.FromPercent(10m),
            SyntheticSpreadBasisPoints: 20m,
            new PaperExecutionPolicy(
                TimeSpan.FromMilliseconds(100),
                Percentage.FromPercent(0.1m),
                SlippageBasisPoints: 10m,
                Percentage.FromPercent(5m)),
            instrumentRules,
            new VolatilityAdjustedExecutionPolicy(
                MinimumSpreadBasisPoints: 2m,
                MaximumSpreadBasisPoints: 100m,
                MinimumSlippageBasisPoints: 1m,
                MaximumSlippageBasisPoints: 150m,
                VolatilitySpreadMultiplier: 1m,
                VolatilitySlippageMultiplier: 2m,
                ParticipationSpreadAtLimitBasisPoints: 5m,
                ParticipationPenaltyAtLimitBasisPoints: 20m,
                TwapChildOrderCount: 4));
        var grid = AtrHysteresisParameterGrid.Create(
            AtrHysteresisParameterCandidate.Create(7, 0.1m),
            AtrHysteresisParameterCandidate.Create(7, 0.2m),
            AtrHysteresisParameterCandidate.Create(7, 0.3m),
            AtrHysteresisParameterCandidate.Create(14, 0.1m),
            AtrHysteresisParameterCandidate.Create(14, 0.2m),
            AtrHysteresisParameterCandidate.Create(14, 0.3m),
            AtrHysteresisParameterCandidate.Create(21, 0.1m),
            AtrHysteresisParameterCandidate.Create(21, 0.2m),
            AtrHysteresisParameterCandidate.Create(21, 0.3m));
        return new LockedAtrHysteresisV6Configuration(
            baseline,
            candidate,
            execution,
            grid);
    }
}
