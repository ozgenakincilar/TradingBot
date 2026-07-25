using System.Runtime.CompilerServices;
using TradingBot.Domain.MarketData;

namespace TradingBot.Application.Backtesting;

public static class BacktestPartitionedCandleStream
{
    public static async IAsyncEnumerable<Candle> ReadAsync(
        IAsyncEnumerable<Candle> source,
        ChronologicalDatasetSplit split,
        BacktestExperimentPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(plan);
        var allowed = plan.Partitions.ToHashSet();

        await foreach (var candle in source.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partition = split.Classify(candle.OpenTime);
            if (partition == BacktestDatasetPartition.Excluded)
            {
                continue;
            }

            if (allowed.Contains(partition))
            {
                yield return candle;
            }
        }
    }
}
