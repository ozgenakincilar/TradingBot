using TradingBot.Domain.Common;
using TradingBot.Research;

namespace TradingBot.Research.Tests;

public sealed class ResearchExportCommandTests
{
    [Fact]
    public void ValidCommandMapsStrictUtcResearchRequest()
    {
        var result = ResearchExportCommand.Parse(ValidArguments());

        Assert.Equal("OKX", result.InstrumentId.Exchange);
        Assert.Equal("BTC-USDT", result.InstrumentId.Symbol);
        Assert.Equal(TimeSpan.FromMinutes(15), result.Timeframe.Duration);
        Assert.Equal(TimeSpan.Zero, result.FromInclusive.Offset);
        Assert.Equal("fixture-v1", result.SourceId);
        Assert.Equal("data/fixture.csv", result.OutputPath);
    }

    [Theory]
    [InlineData(0, "unknown")]
    [InlineData(2, "BTCUSDT")]
    [InlineData(4, "5m")]
    [InlineData(6, "2025-01-01T03:00:00.0000000+03:00")]
    [InlineData(12, "")]
    public void UnknownOrUnsafeArgumentIsRejected(int valueIndex, string replacement)
    {
        var arguments = ValidArguments();
        arguments[valueIndex] = replacement;

        var action = () => ResearchExportCommand.Parse(arguments);

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static string[] ValidArguments() =>
    [
        "export-candles",
        "--instrument", "BTC-USDT",
        "--timeframe", "15m",
        "--from", "2025-01-01T00:00:00.0000000+00:00",
        "--to", "2025-01-02T00:00:00.0000000+00:00",
        "--source", "fixture-v1",
        "--output", "data/fixture.csv"
    ];
}
