using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Orders;

namespace TradingBot.Application.Backtesting;

public sealed record BuyAndHoldBenchmarkReport(
    decimal InitialQuoteBalance,
    decimal AllocatedQuoteBalance,
    decimal EndingCashBalance,
    decimal BaseQuantity,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal NetLiquidationValue,
    decimal GrossReturnPercent,
    decimal NetReturnPercent,
    decimal TotalFees,
    decimal EstimatedSpreadCost,
    decimal EstimatedSlippageCost,
    decimal MaximumDrawdownPercent,
    int CandleCount,
    DateTimeOffset EntryAt,
    DateTimeOffset ExitAt);

public sealed class BuyAndHoldBenchmark
{
    public Task<BuyAndHoldBenchmarkReport> RunAsync(
        IAsyncEnumerable<Candle> candles,
        ChronologicalDatasetSplit split,
        BacktestExecutionPolicy policy,
        InstrumentId instrumentId,
        Timeframe signalTimeframe,
        CancellationToken cancellationToken)
        => RunRangeAsync(
            candles,
            split.ValidationEndExclusive,
            split.OutOfSampleEndExclusive,
            policy,
            instrumentId,
            signalTimeframe,
            cancellationToken);

    public async Task<BuyAndHoldBenchmarkReport> RunRangeAsync(
        IAsyncEnumerable<Candle> candles,
        DateTimeOffset evaluationStartInclusive,
        DateTimeOffset evaluationEndExclusive,
        BacktestExecutionPolicy policy,
        InstrumentId instrumentId,
        Timeframe signalTimeframe,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentNullException.ThrowIfNull(policy);
        if (instrumentId == default || evaluationStartInclusive == default ||
            evaluationEndExclusive == default ||
            evaluationStartInclusive.Offset != TimeSpan.Zero ||
            evaluationEndExclusive.Offset != TimeSpan.Zero ||
            evaluationStartInclusive >= evaluationEndExclusive ||
            !signalTimeframe.IsBoundary(evaluationStartInclusive) ||
            !signalTimeframe.IsBoundary(evaluationEndExclusive))
        {
            throw new DomainRuleViolationException("Benchmark identity and evaluation range are invalid.");
        }

        policy.Validate(signalTimeframe, instrumentId);
        if (policy.DynamicExecution is not null)
        {
            throw new DomainRuleViolationException(
                "Dynamic execution cannot enter walk-forward acceptance until benchmark cost parity is implemented.");
        }

        Candle? first = null;
        Candle? previous = null;
        var state = new State(policy);
        await foreach (var candle in candles.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candle.InstrumentId != instrumentId || candle.Timeframe != signalTimeframe)
            {
                throw new DomainRuleViolationException(
                    "Benchmark candles must use the configured instrument and signal timeframe.");
            }

            if (candle.OpenTime < evaluationStartInclusive)
            {
                continue;
            }

            if (candle.OpenTime >= evaluationEndExclusive)
            {
                break;
            }

            if (first is null)
            {
                if (candle.OpenTime != evaluationStartInclusive)
                {
                    throw new DomainRuleViolationException(
                        "Benchmark OOS candles must start at the split boundary.");
                }

                first = candle;
                state.Enter(candle);
            }
            else if (previous is not null && candle.OpenTime != previous.CloseTime)
            {
                throw new DomainRuleViolationException(
                    "Benchmark OOS candles must be contiguous.");
            }

            state.Observe(candle);
            previous = candle;
        }

        if (first is null || previous is null ||
            previous.CloseTime != evaluationEndExclusive)
        {
            throw new DomainRuleViolationException(
                "Benchmark requires one complete OOS candle window.");
        }

        return state.CreateReport(first, previous);
    }

    private sealed class State
    {
        private readonly BacktestExecutionPolicy _policy;
        private readonly decimal _initial;
        private decimal _peakEquity;
        private decimal _maximumDrawdownPercent;
        private decimal _buyFee;
        private decimal _buySpreadCost;
        private decimal _buySlippageCost;
        private int _candleCount;

        public State(BacktestExecutionPolicy policy)
        {
            _policy = policy;
            _initial = policy.InitialQuoteBalance;
            _peakEquity = _initial;
        }

        public decimal Allocated { get; private set; }

        public decimal Cash { get; private set; }

        public decimal Quantity { get; private set; }

        public decimal EntryPrice { get; private set; }

        public void Enter(Candle candle)
        {
            var allocationBudget = Checked(_initial * _policy.QuoteAllocation.Fraction);
            var ask = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Buy,
                ApplyHalfSpread(candle.Open, isBuy: true));
            EntryPrice = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Buy,
                ApplySlippage(ask, isBuy: true));
            var unitCost = Checked(EntryPrice *
                (1m + _policy.PaperExecution.CommissionRate.Fraction));
            Quantity = BacktestInstrumentQuantization.NormalizeQuantity(
                _policy,
                allocationBudget / unitCost);
            if (!BacktestInstrumentQuantization.IsTradable(_policy, EntryPrice, Quantity))
            {
                throw new DomainRuleViolationException(
                    "Benchmark allocation is not tradable under the instrument rules.");
            }

            _buyFee = Checked(EntryPrice * Quantity *
                _policy.PaperExecution.CommissionRate.Fraction);
            Allocated = _policy.InstrumentRules is null
                ? allocationBudget
                : Checked((EntryPrice * Quantity) + _buyFee);
            Cash = Checked(_initial - Allocated);
            _buySpreadCost = Checked((ask - candle.Open) * Quantity);
            _buySlippageCost = Checked((EntryPrice - ask) * Quantity);
        }

        public void Observe(Candle candle)
        {
            _candleCount = Increment(_candleCount);
            var liquidation = Liquidate(candle.Close, requireTradable: false);
            _peakEquity = Math.Max(_peakEquity, liquidation.Value);
            var drawdown = Checked(((_peakEquity - liquidation.Value) / _peakEquity) * 100m);
            _maximumDrawdownPercent = Math.Max(_maximumDrawdownPercent, drawdown);
        }

        public BuyAndHoldBenchmarkReport CreateReport(Candle first, Candle last)
        {
            var liquidation = Liquidate(last.Close, requireTradable: true);
            var totalFees = Checked(_buyFee + liquidation.Fee);
            var spreadCost = Checked(_buySpreadCost + liquidation.SpreadCost);
            var slippageCost = Checked(_buySlippageCost + liquidation.SlippageCost);
            var netReturn = Checked(((liquidation.Value - _initial) / _initial) * 100m);
            var grossReturn = Checked(((liquidation.Value + totalFees + spreadCost +
                slippageCost - _initial) / _initial) * 100m);
            return new BuyAndHoldBenchmarkReport(
                _initial,
                Allocated,
                Cash,
                Quantity,
                EntryPrice,
                liquidation.Price,
                liquidation.Value,
                grossReturn,
                netReturn,
                totalFees,
                spreadCost,
                slippageCost,
                _maximumDrawdownPercent,
                _candleCount,
                first.OpenTime,
                last.CloseTime);
        }

        private Liquidation Liquidate(decimal midPrice, bool requireTradable)
        {
            var bid = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Sell,
                ApplyHalfSpread(midPrice, isBuy: false));
            var price = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Sell,
                ApplySlippage(bid, isBuy: false));
            if (requireTradable &&
                !BacktestInstrumentQuantization.IsTradable(_policy, price, Quantity))
            {
                throw new DomainRuleViolationException(
                    "Benchmark liquidation is not tradable under the instrument rules.");
            }

            var gross = Checked(price * Quantity);
            var fee = Checked(gross * _policy.PaperExecution.CommissionRate.Fraction);
            return new Liquidation(
                Checked(Cash + gross - fee),
                price,
                fee,
                Checked((midPrice - bid) * Quantity),
                Checked((bid - price) * Quantity));
        }

        private decimal ApplyHalfSpread(decimal price, bool isBuy)
        {
            var fraction = _policy.SyntheticSpreadBasisPoints / 20_000m;
            return Checked(isBuy ? price * (1m + fraction) : price * (1m - fraction));
        }

        private decimal ApplySlippage(decimal price, bool isBuy)
        {
            var fraction = _policy.PaperExecution.SlippageBasisPoints / 10_000m;
            return Checked(isBuy ? price * (1m + fraction) : price * (1m - fraction));
        }
    }

    private sealed record Liquidation(
        decimal Value,
        decimal Price,
        decimal Fee,
        decimal SpreadCost,
        decimal SlippageCost);

    private static decimal Checked(decimal value)
    {
        try
        {
            return checked(value);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Benchmark financial calculation exceeded decimal bounds.");
        }
    }

    private static int Increment(int value)
    {
        try
        {
            return checked(value + 1);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Benchmark candle count overflowed.");
        }
    }
}
