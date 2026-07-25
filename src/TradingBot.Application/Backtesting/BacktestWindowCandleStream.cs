using System.Runtime.CompilerServices;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public static class BacktestWindowCandleStream
{
    public static async IAsyncEnumerable<Candle> ReadAsync(
        IAsyncEnumerable<Candle> source,
        ChronologicalDatasetSplit split,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(split);
        await foreach (var candle in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candle.OpenTime >= split.StartInclusive &&
                candle.OpenTime < split.OutOfSampleEndExclusive)
            {
                yield return candle;
            }
        }
    }
}
