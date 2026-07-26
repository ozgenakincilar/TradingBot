using TradingBot.Application.Backtesting;

namespace TradingBot.Application.Tests;

public sealed class AdxRegimeValidationAcceptanceTests
{
    [Fact]
    public void ExactlyLockedThresholdsAreAcceptedWhenReturnIsPositive()
    {
        var result = AdxRegimeValidationAcceptanceEvaluator.Evaluate(
            20m, 20m, 30, 11m, 10m, 0.01m, 0m, 5m, 60m);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void AnyFailedGateRejectsCandidate()
    {
        var result = AdxRegimeValidationAcceptanceEvaluator.Evaluate(
            19.99m, 20m, 30, 11m, 10m, 0.01m, 0m, 5m, 60m);

        Assert.False(result.IsAccepted);
        Assert.False(result.TradeReductionPassed);
    }

    [Fact]
    public void NoTradesCannotPassProfitFactorOrMinimumActivity()
    {
        var result = AdxRegimeValidationAcceptanceEvaluator.Evaluate(
            100m, 100m, 0, 0m, 0m, 1m, 1m, 0m, 100m);

        Assert.False(result.MinimumTradesPassed);
        Assert.False(result.ProfitFactorPassed);
        Assert.False(result.IsAccepted);
    }
}
