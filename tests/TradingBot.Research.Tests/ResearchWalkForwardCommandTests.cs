using TradingBot.Domain.Common;
using TradingBot.Research;

namespace TradingBot.Research.Tests;

public sealed class ResearchWalkForwardCommandTests
{
    [Fact]
    public void ValidCommandMapsVersionedBaselineAndSchedule()
    {
        var result = ResearchWalkForwardCommand.Parse(ValidArguments());

        Assert.Equal("btc-usdt-long-flat-baseline", result.Definition.StrategyId);
        Assert.Equal(1, result.Definition.Version);
        Assert.Equal(20, result.Definition.SignalEmaPeriod);
        Assert.Equal(200, result.Definition.TrendEmaPeriod);
        Assert.Equal(1_000m, result.ExecutionPolicy.InitialQuoteBalance);
        Assert.Equal(0.10m, result.ExecutionPolicy.QuoteAllocation.Fraction);
        Assert.Null(result.ExecutionPolicy.InstrumentRules);
        Assert.Equal(42, result.RandomSeed);
        Assert.Equal(2, result.Schedule.Windows.Count);
    }

    [Theory]
    [InlineData(0, "unknown")]
    [InlineData(2, "ETH-USDT")]
    [InlineData(4, "data/signal.txt")]
    [InlineData(12, "2025-01-01T03:00:00.0000000+03:00")]
    [InlineData(16, "0")]
    [InlineData(22, "not-an-int")]
    [InlineData(24, "")]
    public void UnknownOrUnsafeArgumentIsRejected(int valueIndex, string replacement)
    {
        var arguments = ValidArguments();
        arguments[valueIndex] = replacement;

        var action = () => ResearchWalkForwardCommand.Parse(arguments);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void DuplicateOptionIsRejected()
    {
        var arguments = ValidArguments();
        arguments[3] = "--instrument";

        var action = () => ResearchWalkForwardCommand.Parse(arguments);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void ValidationCommandCreatesLockedV1V2Comparison()
    {
        var arguments = ValidArguments();
        arguments[0] = "validate-hysteresis-v2";

        var result = ResearchWalkForwardCommand.ParseValidation(arguments);

        Assert.Equal(1, result.Baseline.Version);
        Assert.Equal(0m, result.Baseline.SignalEmaHysteresisBasisPoints);
        Assert.Equal(2, result.Candidate.Version);
        Assert.Equal(30m, result.Candidate.SignalEmaHysteresisBasisPoints);
    }

    [Fact]
    public void LossDiagnosticsCommandSelectsLockedV2Candidate()
    {
        var arguments = ValidArguments();
        arguments[0] = "diagnose-hysteresis-v2";

        var result = ResearchWalkForwardCommand.ParseLossDiagnostics(arguments);

        Assert.Equal(2, result.Definition.Version);
        Assert.Equal(30m, result.Definition.SignalEmaHysteresisBasisPoints);
        Assert.Equal(2, result.Schedule.Windows.Count);
    }

    [Fact]
    public void ProfitProtectionValidationCommandCreatesLockedV2V3Comparison()
    {
        var arguments = ValidArguments();
        arguments[0] = "validate-profit-protection-v3";

        var result = ResearchWalkForwardCommand.ParseProfitProtectionValidation(arguments);

        Assert.Equal(2, result.Baseline.Version);
        Assert.Equal(3, result.Candidate.Version);
        Assert.Equal(4, result.Candidate.ReentryCooldownCandles);
        Assert.Equal(100m, result.Candidate.ProfitProtectionActivationBasisPoints);
        Assert.Equal(50m, result.Candidate.ProfitProtectionTrailingBasisPoints);
    }

    [Fact]
    public void CompleteInstrumentRulesEnableQuantizedExecution()
    {
        var arguments = ValidArguments()
            .Concat([
                "--tick-size", "0.1",
                "--quantity-step", "0.00000001",
                "--minimum-quantity", "0.00001",
                "--minimum-notional", "1"
            ])
            .ToArray();

        var result = ResearchWalkForwardCommand.Parse(arguments);

        var rules = Assert.IsType<TradingBot.Domain.Instruments.Instrument>(
            result.ExecutionPolicy.InstrumentRules);
        Assert.Equal(0.1m, rules.PriceTickSize);
        Assert.Equal(0.00000001m, rules.QuantityStepSize);
        Assert.Equal(0.00001m, rules.MinimumQuantity);
        Assert.Equal(1m, rules.MinimumNotional);
    }

    [Fact]
    public void PartialOrInvalidInstrumentRulesAreRejected()
    {
        var partial = ValidArguments().Concat(["--tick-size", "0.1"]).ToArray();
        var invalid = ValidArguments()
            .Concat([
                "--tick-size", "0.1",
                "--quantity-step", "0",
                "--minimum-quantity", "0.00001",
                "--minimum-notional", "1"
            ])
            .ToArray();

        Assert.Throws<DomainRuleViolationException>(
            () => ResearchWalkForwardCommand.Parse(partial));
        Assert.Throws<DomainRuleViolationException>(
            () => ResearchWalkForwardCommand.Parse(invalid));
    }

    private static string[] ValidArguments() =>
    [
        "run-walk-forward",
        "--instrument", "BTC-USDT",
        "--signal", "data/signal.csv",
        "--signal-source", "okx-signal-v1",
        "--trend", "data/trend.csv",
        "--trend-source", "okx-trend-v1",
        "--from", "2025-01-01T00:00:00.0000000+00:00",
        "--to", "2025-09-28T00:00:00.0000000+00:00",
        "--training-days", "180",
        "--validation-days", "30",
        "--oos-days", "30",
        "--mode", "rolling",
        "--seed", "42"
    ];
}
