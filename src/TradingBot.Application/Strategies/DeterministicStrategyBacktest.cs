using System.Runtime.CompilerServices;
using TradingBot.Domain.Common;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Strategies;

public sealed record StrategyBacktestDecision(
    StrategyDecision Decision,
    StrategyPositionState PositionAfterDecision,
    Candle SignalCandle);

public sealed class DeterministicStrategyBacktest
{
    public async IAsyncEnumerable<StrategyBacktestDecision> RunAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<Candle> signalCandles,
        IAsyncEnumerable<Candle> trendCandles,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var decision in RunCoreAsync(
                           definition,
                           signalCandles,
                           trendCandles,
                           null,
                           cancellationToken))
        {
            yield return decision;
        }
    }

    public async IAsyncEnumerable<StrategyBacktestDecision> RunAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<Candle> signalCandles,
        IAsyncEnumerable<Candle> trendCandles,
        DateTimeOffset evaluationStartInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(signalCandles);
        ArgumentNullException.ThrowIfNull(trendCandles);
        if (evaluationStartInclusive == default ||
            evaluationStartInclusive.Offset != TimeSpan.Zero ||
            !definition.SignalTimeframe.IsBoundary(evaluationStartInclusive) ||
            !definition.TrendTimeframe.IsBoundary(evaluationStartInclusive))
        {
            throw new DomainRuleViolationException(
                "Backtest evaluation start must be UTC and align to both timeframes.");
        }

        await foreach (var decision in RunCoreAsync(
                           definition,
                           signalCandles,
                           trendCandles,
                           evaluationStartInclusive,
                           cancellationToken))
        {
            yield return decision;
        }
    }

    private static async IAsyncEnumerable<StrategyBacktestDecision> RunCoreAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<Candle> signalCandles,
        IAsyncEnumerable<Candle> trendCandles,
        DateTimeOffset? evaluationStartInclusive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {

        var signalWindow = new BoundedCandleWindow(
            definition.SignalTimeframe,
            definition.MinimumSignalWarmupCandles);
        var trendWindow = new BoundedCandleWindow(
            definition.TrendTimeframe,
            definition.MinimumTrendWarmupCandles);
        var position = StrategyPositionState.Flat;

        await using var trendEnumerator = trendCandles.GetAsyncEnumerator(cancellationToken);
        var hasTrend = await trendEnumerator.MoveNextAsync();
        await foreach (var signal in signalCandles.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (hasTrend && trendEnumerator.Current.CloseTime <= signal.CloseTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                trendWindow.Append(definition, trendEnumerator.Current);
                hasTrend = await trendEnumerator.MoveNextAsync();
            }

            signalWindow.Append(definition, signal);
            if (evaluationStartInclusive is { } start && signal.OpenTime < start)
            {
                continue;
            }

            if (signalWindow.Count < definition.MinimumSignalWarmupCandles ||
                trendWindow.Count < definition.MinimumTrendWarmupCandles)
            {
                continue;
            }

            var decision = LongFlatStrategyEvaluator.Evaluate(
                definition,
                signalWindow.Candles,
                trendWindow.Candles,
                position);
            position = decision.Action switch
            {
                StrategyAction.EnterLong => StrategyPositionState.Long,
                StrategyAction.ExitToFlat => StrategyPositionState.Flat,
                _ => position
            };
            yield return new StrategyBacktestDecision(decision, position, signal);
        }
    }

    private sealed class BoundedCandleWindow(Timeframe timeframe, int capacity)
    {
        private readonly List<Candle> _candles = new(capacity);

        public int Count => _candles.Count;

        public IReadOnlyList<Candle> Candles => _candles;

        public void Append(StrategyDefinition definition, Candle candle)
        {
            ArgumentNullException.ThrowIfNull(candle);
            var expectedInstrument = definition.InstrumentId;
            if (candle.InstrumentId != expectedInstrument || candle.Timeframe != timeframe)
            {
                throw new DomainRuleViolationException("Backtest candle identity is invalid.");
            }

            if (_candles.Count > 0 && candle.OpenTime != _candles[^1].CloseTime)
            {
                throw new DomainRuleViolationException("Backtest candle input must be contiguous and ordered.");
            }

            _candles.Add(candle);
            if (_candles.Count > capacity)
            {
                _candles.RemoveAt(0);
            }
        }
    }
}
