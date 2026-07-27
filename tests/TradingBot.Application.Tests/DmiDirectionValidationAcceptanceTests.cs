using TradingBot.Application.Backtesting;

namespace TradingBot.Application.Tests;

public sealed class DmiDirectionValidationAcceptanceTests
{
    [Fact]
    public void AllLockedGatesMustPass()
    {
        var result = DmiDirectionValidationAcceptanceEvaluator.Evaluate(
            40, 30, 20m, 10m, 1.09m, 1.10m, 0.01m, 0m, 5m, 60m);

        Assert.True(result.IsAccepted);
    }

    [Fact]
    public void IneffectiveDirectionFilterIsRejected()
    {
        var result = DmiDirectionValidationAcceptanceEvaluator.Evaluate(
            30, 30, 20m, 10m, 1m, 1.2m, 1m, 1m, 1m, 100m);

        Assert.False(result.TradeReductionPassed);
        Assert.False(result.IsAccepted);
    }
}
