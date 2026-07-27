using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

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
        Candle? first = null;
        Candle? previous = null;
        Candle? completedReference = null;
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
                if (candle.CloseTime <= evaluationStartInclusive)
                {
                    completedReference = candle;
                }

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
                if (policy.DynamicExecution is null)
                {
                    state.Enter(candle);
                }
                else if (completedReference is null ||
                         completedReference.CloseTime != evaluationStartInclusive)
                {
                    throw new DomainRuleViolationException(
                        "Dynamic benchmark requires the completed candle immediately before its evaluation boundary.");
                }
            }
            else if (previous is not null && candle.OpenTime != previous.CloseTime)
            {
                throw new DomainRuleViolationException(
                    "Benchmark OOS candles must be contiguous.");
            }

            if (policy.DynamicExecution is not null)
            {
                state.EnterDynamic(candle, completedReference!);
            }

            state.Observe(candle);
            previous = candle;
            completedReference = candle;
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
        private readonly DynamicTwapExecutionModel _dynamicExecution = new();
        private readonly decimal _initial;
        private decimal _peakEquity;
        private decimal _maximumDrawdownPercent;
        private decimal _buyFee;
        private decimal _buySpreadCost;
        private decimal _buySlippageCost;
        private decimal _remainingEntryBudget;
        private decimal _entryNotional;
        private int _candleCount;

        public State(BacktestExecutionPolicy policy)
        {
            _policy = policy;
            _initial = policy.InitialQuoteBalance;
            _peakEquity = _initial;
            Cash = _initial;
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

        public void EnterDynamic(Candle executionCandle, Candle completedReference)
        {
            var dynamicExecution = _policy.DynamicExecution!.Value;
            if (_remainingEntryBudget == 0m && Quantity == 0m)
            {
                _remainingEntryBudget = Checked(
                    _initial * _policy.QuoteAllocation.Fraction);
            }

            if (_remainingEntryBudget <= 0m || Cash <= 0m)
            {
                return;
            }

            var maximumPrice = DynamicTwapExecutionModel.CalculateMaximumExecutionPrice(
                _policy,
                in dynamicExecution,
                executionCandle.Open,
                OrderSide.Buy);
            var unitCost = Checked(maximumPrice *
                (1m + _policy.PaperExecution.CommissionRate.Fraction));
            var requestedQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
                _policy,
                Math.Min(_remainingEntryBudget, Cash) / unitCost);
            if (requestedQuantity <= 0m)
            {
                _remainingEntryBudget = 0m;
                return;
            }

            var request = new DynamicTwapExecutionRequest(
                executionCandle.InstrumentId,
                _policy.QuoteAsset,
                OrderSide.Buy,
                CompletedCandleExecutionReference.Create(completedReference),
                executionCandle.Open,
                requestedQuantity,
                completedReference.CloseTime,
                executionCandle.OpenTime,
                executionCandle.Timeframe.Duration);
            var consumer = new BenchmarkEntryFillConsumer(this);
            _dynamicExecution.Execute(
                _policy,
                in dynamicExecution,
                in request,
                ref consumer);
            if (!DynamicTwapExecutionModel.HasTradableEntryRemainder(
                    _policy,
                    in dynamicExecution,
                    executionCandle.Open,
                    _remainingEntryBudget,
                    Cash))
            {
                _remainingEntryBudget = 0m;
            }

            Allocated = Checked(_initial - Cash);
            EntryPrice = Quantity == 0m ? 0m : Checked(_entryNotional / Quantity);
        }

        public void Observe(Candle candle)
        {
            _candleCount = Increment(_candleCount);
            var liquidation = _policy.DynamicExecution is null
                ? Liquidate(candle.Close, requireTradable: false)
                : MarkDynamic(candle);
            _peakEquity = Math.Max(_peakEquity, liquidation.Value);
            var drawdown = Checked(((_peakEquity - liquidation.Value) / _peakEquity) * 100m);
            _maximumDrawdownPercent = Math.Max(_maximumDrawdownPercent, drawdown);
        }

        public BuyAndHoldBenchmarkReport CreateReport(Candle first, Candle last)
        {
            if (_policy.DynamicExecution is not null && Quantity <= 0m)
            {
                throw new DomainRuleViolationException(
                    "Dynamic benchmark could not establish a tradable position.");
            }

            var liquidation = _policy.DynamicExecution is null
                ? Liquidate(last.Close, requireTradable: true)
                : LiquidateDynamic(last);
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

        private Liquidation MarkDynamic(Candle candle)
        {
            if (Quantity <= 0m)
            {
                return new Liquidation(Cash, candle.Close, 0m, 0m, 0m);
            }

            var dynamicExecution = _policy.DynamicExecution!.Value;
            var input = new ExecutionCostInput(
                candle.Close,
                candle.High,
                candle.Low,
                candle.BaseVolume,
                Quantity,
                _policy.PaperExecution.MaximumLiquidityParticipation.Fraction);
            var quote = VolatilityAdjustedExecutionCostModel.CalculateValidated(
                in dynamicExecution,
                in input);
            return LiquidateWithCosts(
                candle.Close,
                Quantity,
                quote.SpreadBasisPoints,
                quote.SlippageBasisPoints);
        }

        private Liquidation LiquidateDynamic(Candle last)
        {
            var dynamicExecution = _policy.DynamicExecution!.Value;
            var maximumPrice = DynamicTwapExecutionModel.CalculateMaximumExecutionPrice(
                _policy,
                in dynamicExecution,
                last.Close,
                OrderSide.Sell);
            if (!BacktestInstrumentQuantization.IsTradable(
                    _policy,
                    maximumPrice,
                    Quantity))
            {
                throw new DomainRuleViolationException(
                    "Dynamic benchmark liquidation is not tradable under the instrument rules.");
            }

            var request = new DynamicTwapExecutionRequest(
                last.InstrumentId,
                _policy.QuoteAsset,
                OrderSide.Sell,
                CompletedCandleExecutionReference.Create(last),
                last.Close,
                Quantity,
                last.CloseTime,
                last.CloseTime,
                last.Timeframe.Duration);
            var consumer = new BenchmarkExitFillConsumer();
            var summary = _dynamicExecution.Execute(
                _policy,
                in dynamicExecution,
                in request,
                ref consumer);
            if (summary.FilledQuantity != Quantity || consumer.Quantity != Quantity)
            {
                throw new DomainRuleViolationException(
                    "Dynamic benchmark terminal TWAP could not liquidate the complete position within the 5% participation limit.");
            }

            return new Liquidation(
                Checked(Cash + consumer.Proceeds),
                Checked(consumer.Notional / consumer.Quantity),
                consumer.Fee,
                consumer.SpreadCost,
                consumer.SlippageCost);
        }

        private Liquidation LiquidateWithCosts(
            decimal midPrice,
            decimal quantity,
            decimal spreadBasisPoints,
            decimal slippageBasisPoints)
        {
            var bid = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Sell,
                Checked(midPrice * (1m - spreadBasisPoints / 20_000m)));
            var price = BacktestInstrumentQuantization.NormalizePrice(
                _policy,
                OrderSide.Sell,
                Checked(bid * (1m - slippageBasisPoints / 10_000m)));
            var gross = Checked(price * quantity);
            var fee = Checked(gross * _policy.PaperExecution.CommissionRate.Fraction);
            return new Liquidation(
                Checked(Cash + gross - fee),
                price,
                fee,
                Checked((midPrice - bid) * quantity),
                Checked((bid - price) * quantity));
        }

        private void ApplyEntryFill(
            PaperTopOfBookSnapshot market,
            PaperFill fill)
        {
            var requestedCost = Checked(
                fill.Price.Value * fill.Quantity.Value + fill.QuoteFee.Amount);
            var totalCost = DynamicTwapExecutionModel.ClampQuoteDebit(
                requestedCost,
                Math.Min(Cash, _remainingEntryBudget));

            Cash = Checked(Cash - totalCost);
            _remainingEntryBudget = Math.Max(
                0m,
                Checked(_remainingEntryBudget - totalCost));
            Quantity = Checked(Quantity + fill.Quantity.Value);
            _entryNotional = Checked(
                _entryNotional + fill.Price.Value * fill.Quantity.Value);
            _buyFee = Checked(_buyFee + fill.QuoteFee.Amount);
            var mid = Checked((market.BestAsk.Value + market.BestBid.Value) / 2m);
            _buySpreadCost = Checked(_buySpreadCost +
                Math.Abs(market.BestAsk.Value - mid) * fill.Quantity.Value);
            _buySlippageCost = Checked(_buySlippageCost +
                Math.Abs(fill.Price.Value - market.BestAsk.Value) * fill.Quantity.Value);
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

        private readonly struct BenchmarkEntryFillConsumer(State state) :
            IDynamicTwapFillConsumer
        {
            public void Accept(
                PaperTopOfBookSnapshot market,
                PaperFill fill,
                OrderSide side)
            {
                if (side != OrderSide.Buy)
                {
                    throw new DomainRuleViolationException(
                        "Dynamic benchmark entry received a non-buy fill.");
                }

                state.ApplyEntryFill(market, fill);
            }
        }

        private struct BenchmarkExitFillConsumer : IDynamicTwapFillConsumer
        {
            public decimal Quantity { get; private set; }

            public decimal Notional { get; private set; }

            public decimal Proceeds { get; private set; }

            public decimal Fee { get; private set; }

            public decimal SpreadCost { get; private set; }

            public decimal SlippageCost { get; private set; }

            public void Accept(
                PaperTopOfBookSnapshot market,
                PaperFill fill,
                OrderSide side)
            {
                if (side != OrderSide.Sell)
                {
                    throw new DomainRuleViolationException(
                        "Dynamic benchmark exit received a non-sell fill.");
                }

                var fillNotional = Checked(fill.Price.Value * fill.Quantity.Value);
                Quantity = Checked(Quantity + fill.Quantity.Value);
                Notional = Checked(Notional + fillNotional);
                Fee = Checked(Fee + fill.QuoteFee.Amount);
                Proceeds = Checked(Proceeds + fillNotional - fill.QuoteFee.Amount);
                var mid = Checked((market.BestAsk.Value + market.BestBid.Value) / 2m);
                SpreadCost = Checked(SpreadCost +
                    Math.Abs(mid - market.BestBid.Value) * fill.Quantity.Value);
                SlippageCost = Checked(SlippageCost +
                    Math.Abs(market.BestBid.Value - fill.Price.Value) * fill.Quantity.Value);
            }
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
