using TradingBot.Application.Abstractions;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Infrastructure.Tests;

public sealed class LockedV6ForwardEvidenceEvaluatorTests
{
    [Fact]
    public async Task EvaluationDoesNotOpenBeforeSevenImmutablePartitionsExist()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"tradingbot-forward-evaluator-{Guid.NewGuid():N}");
        var evaluator = new LockedV6ForwardEvidenceEvaluator(
            new RejectingCatalog(),
            root,
            minimumNotional: 1m);
        var policy = new ForwardEvidencePolicy(
            "btc-usdt-v6-forward",
            InstrumentId.Create("OKX", "BTC-USDT"),
            Timeframe.Create(TimeSpan.FromMinutes(15)),
            Timeframe.Create(TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero));

        var result = await evaluator.EvaluateAsync(
            policy,
            [],
            policy.StartInclusive,
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(Directory.Exists(root));
    }

    private sealed class RejectingCatalog : ISpotInstrumentCatalog
    {
        public ValueTask<SpotInstrumentMetadata> GetAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Instrument metadata must not be requested before seven partitions.");
    }
}
