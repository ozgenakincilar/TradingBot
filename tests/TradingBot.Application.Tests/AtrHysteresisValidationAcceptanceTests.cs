using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;

namespace TradingBot.Application.Tests;

public sealed class AtrHysteresisValidationAcceptanceTests
{
    [Fact]
    public void EveryPreRegisteredGateMustPass()
    {
        var result = AtrHysteresisValidationAcceptanceEvaluator.Evaluate(
            completedTrades: 30,
            baselineProfitFactorScore: 1.10m,
            candidateProfitFactorScore: 1.11m,
            compoundedNetReturnPercent: 0.01m,
            benchmarkExcessPercent: 0m,
            worstDrawdownPercent: 5m,
            profitableWindowPercent: 60m,
            totalExecutionCost: 9.99m,
            grossBeforeCostProfit: 10m,
            fullyExecuted: true);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void FailedMetricsRejectAllIndependentGates()
    {
        var result = AtrHysteresisValidationAcceptanceEvaluator.Evaluate(
            completedTrades: 29,
            baselineProfitFactorScore: 1.10m,
            candidateProfitFactorScore: 1.10m,
            compoundedNetReturnPercent: 0m,
            benchmarkExcessPercent: -0.01m,
            worstDrawdownPercent: 5.01m,
            profitableWindowPercent: 59.99m,
            totalExecutionCost: 10m,
            grossBeforeCostProfit: 10m,
            fullyExecuted: false);

        Assert.False(result.IsAccepted);
        Assert.False(result.MinimumTradesPassed);
        Assert.False(result.ProfitFactorPassed);
        Assert.False(result.PositiveNetReturnPassed);
        Assert.False(result.BenchmarkExcessPassed);
        Assert.False(result.DrawdownPassed);
        Assert.False(result.ProfitableWindowsPassed);
        Assert.False(result.ExecutionCostCoveragePassed);
        Assert.False(result.FullyExecutedPassed);
    }

    [Fact]
    public void InvalidMetricsFailClosed()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            AtrHysteresisValidationAcceptanceEvaluator.Evaluate(
                -1, 1m, 2m, 1m, 1m, 1m, 60m, 1m, 2m, true));
    }
}
