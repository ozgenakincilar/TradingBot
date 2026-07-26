using System.Runtime.CompilerServices;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public static class BacktestEvaluationCandleStream
{
    public static async IAsyncEnumerable<Candle> ReadAsync(
        IAsyncEnumerable<Candle> source,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (startInclusive == default || endExclusive == default ||
            startInclusive.Offset != TimeSpan.Zero || endExclusive.Offset != TimeSpan.Zero ||
            startInclusive >= endExclusive)
        {
            throw new DomainRuleViolationException("Backtest evaluation range must be ordered UTC.");
        }

        await foreach (var candle in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candle.OpenTime >= startInclusive && candle.OpenTime < endExclusive)
            {
                yield return candle;
            }
        }
    }
}
