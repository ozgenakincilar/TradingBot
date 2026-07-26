using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Strategies;

public sealed record BacktestExecutionPolicy(
    decimal InitialQuoteBalance,
    AssetCode BaseAsset,
    AssetCode QuoteAsset,
    Percentage QuoteAllocation,
    decimal SyntheticSpreadBasisPoints,
    PaperExecutionPolicy PaperExecution,
    Instrument? InstrumentRules = null)
{
    public void Validate(Timeframe signalTimeframe, InstrumentId? expectedInstrumentId = null)
    {
        if (InitialQuoteBalance <= 0m || BaseAsset == default || QuoteAsset == default ||
            BaseAsset == QuoteAsset || QuoteAllocation.Fraction <= 0m)
        {
            throw new DomainRuleViolationException("Backtest capital, assets, and allocation are invalid.");
        }

        if (SyntheticSpreadBasisPoints is < 0m or > 1_000m)
        {
            throw new DomainRuleViolationException(
                "Backtest synthetic spread must be between 0 and 1,000 basis points.");
        }

        if (InstrumentRules is not null && expectedInstrumentId is { } instrumentId &&
            InstrumentRules.Id != instrumentId)
        {
            throw new DomainRuleViolationException(
                "Backtest instrument rules must match the strategy instrument.");
        }

        PaperExecution.Validate();
        if (PaperExecution.MinimumLatency >= signalTimeframe.Duration)
        {
            throw new DomainRuleViolationException(
                "Backtest fill latency must be shorter than one signal candle.");
        }
    }
}

public sealed record BacktestExecutionReport(
    decimal InitialQuoteBalance,
    decimal EndingCashBalance,
    decimal OpenQuantity,
    decimal NetLiquidationValue,
    decimal GrossReturnPercent,
    decimal NetReturnPercent,
    decimal RealizedPnl,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal? Expectancy,
    decimal TotalFees,
    decimal EstimatedSpreadCost,
    decimal EstimatedSlippageCost,
    decimal MaximumDrawdownPercent,
    int FillCount,
    int CompletedTradeCount,
    int WinningTradeCount,
    decimal? WinRatePercent,
    decimal? ProfitFactor,
    TimeSpan? AverageHoldingTime,
    bool HasPendingExecution,
    DateTimeOffset? FirstFillAt,
    DateTimeOffset? LastFillAt);

public sealed class BacktestExecutionSimulator
{
    private static readonly OrderId BuyOrderId = OrderId.From(
        Guid.Parse("79d98574-b7a2-41b1-8bc0-6536180ff932"));
    private static readonly OrderId SellOrderId = OrderId.From(
        Guid.Parse("e27bc43d-67bd-482e-8f41-9c030ab89fe4"));

    private readonly PaperExecutionEngine _execution = new();

    public async Task<BacktestExecutionReport> RunAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<StrategyBacktestDecision> decisions,
        BacktestExecutionPolicy policy,
        CancellationToken cancellationToken) =>
        (await RunCoreAsync(
            definition,
            decisions,
            policy,
            diagnosticsPolicy: null,
            cancellationToken)).Execution;

    public async Task<BacktestExecutionDiagnosticsReport> RunWithDiagnosticsAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<StrategyBacktestDecision> decisions,
        BacktestExecutionPolicy policy,
        BacktestDiagnosticsPolicy diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnosticsPolicy);
        diagnosticsPolicy.Validate();
        var result = await RunCoreAsync(
            definition,
            decisions,
            policy,
            diagnosticsPolicy,
            cancellationToken);
        return BacktestExecutionDiagnosticsReport.Create(result.Execution, result.Trades!);
    }

    private async Task<SimulationResult> RunCoreAsync(
        StrategyDefinition definition,
        IAsyncEnumerable<StrategyBacktestDecision> decisions,
        BacktestExecutionPolicy policy,
        BacktestDiagnosticsPolicy? diagnosticsPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate(definition.SignalTimeframe, definition.InstrumentId);

        var position = SpotPosition.Open(
            definition.InstrumentId,
            policy.BaseAsset,
            policy.QuoteAsset,
            DateTimeOffset.UnixEpoch);
        var state = new SimulationState(
            policy.InitialQuoteBalance,
            diagnosticsPolicy?.MaximumCompletedTrades);
        Candle? lastCandle = null;

        await foreach (var item in decisions.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDecision(definition, item, lastCandle);
            lastCandle = item.SignalCandle;
            ExecutePendingTarget(definition, policy, item.SignalCandle, position, state);
            ApplyDecisionTarget(item.Decision, position, state, policy);
            if (state.Target != ExecutionTarget.None)
            {
                state.KnownLiquidityQuantity = item.SignalCandle.BaseVolume;
            }

            state.ObserveTradeExcursion(item.SignalCandle, position);
            state.ObserveEquity(CalculateNetLiquidation(policy, item.SignalCandle, position, state.Cash));
        }

        var netLiquidation = lastCandle is null
            ? state.Cash
            : CalculateNetLiquidation(policy, lastCandle, position, state.Cash);
        return new SimulationResult(
            state.CreateReport(position, netLiquidation),
            state.Trades);
    }

    private void ExecutePendingTarget(
        StrategyDefinition definition,
        BacktestExecutionPolicy policy,
        Candle candle,
        SpotPosition position,
        SimulationState state)
    {
        if (state.Target == ExecutionTarget.None)
        {
            return;
        }

        var market = CreateMarket(
            definition.InstrumentId,
            candle,
            state.KnownLiquidityQuantity,
            policy);
        if (state.Target == ExecutionTarget.Long)
        {
            ExecuteBuy(definition, policy, market, position, state);
        }
        else
        {
            ExecuteSell(definition, policy, market, position, state);
        }
    }

    private void ExecuteBuy(
        StrategyDefinition definition,
        BacktestExecutionPolicy policy,
        PaperTopOfBookSnapshot market,
        SpotPosition position,
        SimulationState state)
    {
        if (state.RemainingEntryBudget <= 0m || state.Cash <= 0m)
        {
            state.ClearTarget();
            return;
        }

        var maximumCost = Math.Min(state.RemainingEntryBudget, state.Cash);
        var expectedPrice = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Buy,
            ApplySlippage(
                market.BestAsk.Value,
                OrderSide.Buy,
                policy.PaperExecution.SlippageBasisPoints));
        var unitCost = Checked(expectedPrice * (1m + policy.PaperExecution.CommissionRate.Fraction));
        var requestedQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            maximumCost / unitCost);
        if (!BacktestInstrumentQuantization.IsTradable(policy, expectedPrice, requestedQuantity))
        {
            state.ClearTarget();
            return;
        }

        var result = _execution.Evaluate(
            policy.PaperExecution,
            new PaperExecutionRequest(
                BuyOrderId,
                definition.InstrumentId,
                policy.QuoteAsset,
                OrderSide.Buy,
                OrderType.Market,
                Quantity.From(requestedQuantity),
                null,
                state.TargetSubmittedAt),
            market);
        if (result.Fill is not { } rawFill ||
            BacktestInstrumentQuantization.NormalizeFill(
                policy,
                rawFill,
                OrderSide.Buy) is not { } fill)
        {
            return;
        }

        var totalCost = Checked((fill.Price.Value * fill.Quantity.Value) + fill.QuoteFee.Amount);
        state.Cash = Checked(state.Cash - totalCost);
        state.RemainingEntryBudget = Math.Max(0m, state.RemainingEntryBudget - totalCost);
        position.ApplyBuyFill(fill.Quantity, fill.Price, fill.QuoteFee, fill.OccurredAt);
        state.RecordFill(fill, market, OrderSide.Buy);
        var remainingQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            state.RemainingEntryBudget / unitCost);
        if (state.RemainingEntryBudget <= 0.00000001m || state.Cash <= 0.00000001m ||
            !BacktestInstrumentQuantization.IsTradable(
                policy,
                expectedPrice,
                remainingQuantity))
        {
            state.ClearTarget();
        }
    }

    private void ExecuteSell(
        StrategyDefinition definition,
        BacktestExecutionPolicy policy,
        PaperTopOfBookSnapshot market,
        SpotPosition position,
        SimulationState state)
    {
        if (position.AvailableQuantity <= 0m)
        {
            state.ClearTarget();
            return;
        }

        var requestedQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            position.AvailableQuantity);
        var expectedPrice = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Sell,
            ApplySlippage(
                market.BestBid.Value,
                OrderSide.Sell,
                policy.PaperExecution.SlippageBasisPoints));
        if (!BacktestInstrumentQuantization.IsTradable(policy, expectedPrice, requestedQuantity))
        {
            return;
        }

        var result = _execution.Evaluate(
            policy.PaperExecution,
            new PaperExecutionRequest(
                SellOrderId,
                definition.InstrumentId,
                policy.QuoteAsset,
                OrderSide.Sell,
                OrderType.Market,
                Quantity.From(requestedQuantity),
                null,
                state.TargetSubmittedAt),
            market);
        if (result.Fill is not { } rawFill ||
            BacktestInstrumentQuantization.NormalizeFill(
                policy,
                rawFill,
                OrderSide.Sell) is not { } fill)
        {
            return;
        }

        var openQuantityBefore = position.OpenQuantity;
        position.ReserveForSell(fill.Quantity, fill.OccurredAt);
        position.ApplySellFill(fill.Quantity, fill.Price, fill.QuoteFee, fill.OccurredAt);
        state.Cash = Checked(state.Cash +
            (fill.Price.Value * fill.Quantity.Value) - fill.QuoteFee.Amount);
        state.RecordFill(fill, market, OrderSide.Sell);
        if (position.OpenQuantity >= openQuantityBefore)
        {
            throw new DomainRuleViolationException("Backtest sell fill did not reduce the open position.");
        }

        if (position.OpenQuantity == 0m)
        {
            state.RecordCompletedTrade(
                position.RealizedPnl - state.RealizedPnlAtTradeOpen,
                fill.OccurredAt);
            state.ClearTarget();
        }
    }

    private static void ApplyDecisionTarget(
        StrategyDecision decision,
        SpotPosition position,
        SimulationState state,
        BacktestExecutionPolicy policy)
    {
        if (decision.Action == StrategyAction.EnterLong)
        {
            if (position.OpenQuantity > 0m)
            {
                state.ClearTarget();
                return;
            }

            state.Target = ExecutionTarget.Long;
            state.TargetSubmittedAt = decision.EvaluatedAt;
            state.RemainingEntryBudget = Checked(state.Cash * policy.QuoteAllocation.Fraction);
            state.RealizedPnlAtTradeOpen = position.RealizedPnl;
            state.EntryReasonCode = decision.ReasonCode;
        }
        else if (decision.Action == StrategyAction.ExitToFlat)
        {
            if (position.OpenQuantity == 0m)
            {
                state.ClearTarget();
                return;
            }

            state.Target = ExecutionTarget.Flat;
            state.TargetSubmittedAt = decision.EvaluatedAt;
            state.RemainingEntryBudget = 0m;
            state.ExitReasonCode = decision.ReasonCode;
        }
    }

    private static PaperTopOfBookSnapshot CreateMarket(
        InstrumentId instrumentId,
        Candle candle,
        decimal knownLiquidityQuantity,
        BacktestExecutionPolicy policy)
    {
        var halfSpread = policy.SyntheticSpreadBasisPoints / 20_000m;
        var bid = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Sell,
            Checked(candle.Open * (1m - halfSpread)));
        var ask = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Buy,
            Checked(candle.Open * (1m + halfSpread)));
        return new PaperTopOfBookSnapshot(
            instrumentId,
            Price.From(bid),
            knownLiquidityQuantity,
            Price.From(ask),
            knownLiquidityQuantity,
            candle.OpenTime + policy.PaperExecution.MinimumLatency);
    }

    private static decimal CalculateNetLiquidation(
        BacktestExecutionPolicy policy,
        Candle candle,
        SpotPosition position,
        decimal cash)
    {
        if (position.OpenQuantity == 0m)
        {
            return cash;
        }

        var halfSpread = policy.SyntheticSpreadBasisPoints / 20_000m;
        var bid = Checked(candle.Close * (1m - halfSpread));
        var sellPrice = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Sell,
            ApplySlippage(
                bid,
                OrderSide.Sell,
                policy.PaperExecution.SlippageBasisPoints));
        var gross = Checked(sellPrice * position.OpenQuantity);
        var fee = Checked(gross * policy.PaperExecution.CommissionRate.Fraction);
        return Checked(cash + gross - fee);
    }

    private static decimal ApplySlippage(decimal price, OrderSide side, decimal basisPoints)
    {
        var fraction = basisPoints / 10_000m;
        return Checked(side == OrderSide.Buy
            ? price * (1m + fraction)
            : price * (1m - fraction));
    }

    private static void ValidateDecision(
        StrategyDefinition definition,
        StrategyBacktestDecision item,
        Candle? previous)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Decision);
        ArgumentNullException.ThrowIfNull(item.SignalCandle);
        if (item.SignalCandle.InstrumentId != definition.InstrumentId ||
            item.SignalCandle.Timeframe != definition.SignalTimeframe ||
            item.Decision.StrategyId != definition.StrategyId ||
            item.Decision.StrategyVersion != definition.Version ||
            item.Decision.SignalCandleOpenTime != item.SignalCandle.OpenTime ||
            (item.Decision.Action == StrategyAction.EnterLong &&
             item.PositionAfterDecision != StrategyPositionState.Long) ||
            (item.Decision.Action == StrategyAction.ExitToFlat &&
             item.PositionAfterDecision != StrategyPositionState.Flat) ||
            (previous is not null && item.SignalCandle.OpenTime != previous.CloseTime))
        {
            throw new DomainRuleViolationException(
                "Backtest execution decisions must match a contiguous signal-candle stream.");
        }
    }

    private static decimal Checked(decimal value)
    {
        try
        {
            return checked(value);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException("Backtest financial calculation exceeded decimal bounds.");
        }
    }

    private enum ExecutionTarget
    {
        None = 0,
        Long = 1,
        Flat = 2
    }

    private sealed record SimulationResult(
        BacktestExecutionReport Execution,
        IReadOnlyList<BacktestTradeAttribution>? Trades);

    private sealed class SimulationState
    {
        private readonly decimal _initialBalance;
        private readonly int? _maximumCompletedTrades;
        private readonly List<BacktestTradeAttribution>? _trades;
        private decimal _peakEquity;
        private decimal _maximumDrawdownPercent;
        private decimal _grossProfit;
        private decimal _grossLoss;
        private TimeSpan _totalHoldingTime;
        private decimal _tradeFees;
        private decimal _tradeSpreadCost;
        private decimal _tradeSlippageCost;
        private decimal _entryNotional;
        private decimal _entryQuantity;
        private decimal _exitNotional;
        private decimal _exitQuantity;
        private decimal _maximumFavorableExcursionPercent;
        private decimal _maximumAdverseExcursionPercent;

        public SimulationState(decimal initialBalance, int? maximumCompletedTrades)
        {
            _initialBalance = initialBalance;
            _maximumCompletedTrades = maximumCompletedTrades;
            _trades = maximumCompletedTrades is null ? null : [];
            _peakEquity = initialBalance;
            Cash = initialBalance;
        }

        public decimal Cash { get; set; }

        public ExecutionTarget Target { get; set; }

        public DateTimeOffset TargetSubmittedAt { get; set; }

        public decimal RemainingEntryBudget { get; set; }

        public decimal KnownLiquidityQuantity { get; set; }

        public decimal RealizedPnlAtTradeOpen { get; set; }

        public decimal TotalFees { get; private set; }

        public decimal SpreadCost { get; private set; }

        public decimal SlippageCost { get; private set; }

        public int FillCount { get; private set; }

        public int CompletedTrades { get; private set; }

        public int WinningTrades { get; private set; }

        public DateTimeOffset? FirstFillAt { get; private set; }

        public DateTimeOffset? LastFillAt { get; private set; }

        public DateTimeOffset? TradeOpenedAt { get; private set; }

        public string? EntryReasonCode { get; set; }

        public string? ExitReasonCode { get; set; }

        public IReadOnlyList<BacktestTradeAttribution>? Trades => _trades;

        public void ClearTarget()
        {
            Target = ExecutionTarget.None;
            RemainingEntryBudget = 0m;
        }

        public void RecordFill(PaperFill fill, PaperTopOfBookSnapshot market, OrderSide side)
        {
            var reference = side == OrderSide.Buy ? market.BestAsk.Value : market.BestBid.Value;
            var mid = (market.BestAsk.Value + market.BestBid.Value) / 2m;
            var spreadCost = Math.Abs(reference - mid) * fill.Quantity.Value;
            var slippageCost = Math.Abs(fill.Price.Value - reference) * fill.Quantity.Value;
            TotalFees = Checked(TotalFees + fill.QuoteFee.Amount);
            SpreadCost = Checked(SpreadCost + spreadCost);
            SlippageCost = Checked(SlippageCost + slippageCost);
            FillCount++;
            FirstFillAt ??= fill.OccurredAt;
            LastFillAt = fill.OccurredAt;
            if (side == OrderSide.Buy)
            {
                TradeOpenedAt ??= fill.OccurredAt;
                _entryNotional = Checked(_entryNotional + fill.Price.Value * fill.Quantity.Value);
                _entryQuantity = Checked(_entryQuantity + fill.Quantity.Value);
            }
            else
            {
                _exitNotional = Checked(_exitNotional + fill.Price.Value * fill.Quantity.Value);
                _exitQuantity = Checked(_exitQuantity + fill.Quantity.Value);
            }

            if (_trades is not null)
            {
                _tradeFees = Checked(_tradeFees + fill.QuoteFee.Amount);
                _tradeSpreadCost = Checked(_tradeSpreadCost + spreadCost);
                _tradeSlippageCost = Checked(_tradeSlippageCost + slippageCost);
            }
        }

        public void ObserveTradeExcursion(Candle candle, SpotPosition position)
        {
            if (_trades is null || position.OpenQuantity <= 0m || position.AverageEntryPrice <= 0m)
            {
                return;
            }

            var favorable = ((candle.High - position.AverageEntryPrice) /
                position.AverageEntryPrice) * 100m;
            var adverse = ((position.AverageEntryPrice - candle.Low) /
                position.AverageEntryPrice) * 100m;
            _maximumFavorableExcursionPercent = Math.Max(
                _maximumFavorableExcursionPercent,
                Math.Max(0m, favorable));
            _maximumAdverseExcursionPercent = Math.Max(
                _maximumAdverseExcursionPercent,
                Math.Max(0m, adverse));
        }

        public void RecordCompletedTrade(decimal netPnl, DateTimeOffset closedAt)
        {
            if (TradeOpenedAt is not { } openedAt || closedAt < openedAt)
            {
                throw new DomainRuleViolationException("Backtest completed trade has invalid holding time.");
            }

            CompletedTrades++;
            _totalHoldingTime += closedAt - openedAt;
            if (_trades is not null)
            {
                if (_trades.Count >= _maximumCompletedTrades)
                {
                    throw new DomainRuleViolationException(
                        "Backtest diagnostics completed-trade limit was exceeded.");
                }

                if (EntryReasonCode is null || ExitReasonCode is null ||
                    _entryQuantity <= 0m || _exitQuantity <= 0m)
                {
                    throw new DomainRuleViolationException(
                        "Backtest trade attribution is incomplete.");
                }

                var executionCosts = Checked(
                    _tradeFees + _tradeSpreadCost + _tradeSlippageCost);
                _trades.Add(new BacktestTradeAttribution(
                    CompletedTrades,
                    openedAt,
                    closedAt,
                    EntryReasonCode,
                    ExitReasonCode,
                    _entryNotional / _entryQuantity,
                    _exitNotional / _exitQuantity,
                    _exitQuantity,
                    netPnl,
                    _tradeFees,
                    _tradeSpreadCost,
                    _tradeSlippageCost,
                    Checked(netPnl + executionCosts),
                    _maximumFavorableExcursionPercent,
                    _maximumAdverseExcursionPercent,
                    closedAt - openedAt));
            }

            TradeOpenedAt = null;
            ResetTradeAttribution();
            if (netPnl > 0m)
            {
                WinningTrades++;
                _grossProfit = Checked(_grossProfit + netPnl);
            }
            else if (netPnl < 0m)
            {
                _grossLoss = Checked(_grossLoss + netPnl);
            }
        }

        private void ResetTradeAttribution()
        {
            EntryReasonCode = null;
            ExitReasonCode = null;
            _tradeFees = 0m;
            _tradeSpreadCost = 0m;
            _tradeSlippageCost = 0m;
            _entryNotional = 0m;
            _entryQuantity = 0m;
            _exitNotional = 0m;
            _exitQuantity = 0m;
            _maximumFavorableExcursionPercent = 0m;
            _maximumAdverseExcursionPercent = 0m;
        }

        public void ObserveEquity(decimal equity)
        {
            _peakEquity = Math.Max(_peakEquity, equity);
            if (_peakEquity <= 0m)
            {
                throw new DomainRuleViolationException("Backtest equity peak must remain positive.");
            }

            var drawdown = ((_peakEquity - equity) / _peakEquity) * 100m;
            _maximumDrawdownPercent = Math.Max(_maximumDrawdownPercent, drawdown);
        }

        public BacktestExecutionReport CreateReport(SpotPosition position, decimal netLiquidation)
        {
            var returnPercent = ((netLiquidation - _initialBalance) / _initialBalance) * 100m;
            var grossReturnPercent =
                ((netLiquidation + TotalFees + SpreadCost + SlippageCost - _initialBalance) /
                 _initialBalance) * 100m;
            decimal? winRate = CompletedTrades == 0
                ? null
                : (decimal?)WinningTrades / CompletedTrades * 100m;
            decimal? profitFactor = _grossLoss == 0m
                ? null
                : _grossProfit / Math.Abs(_grossLoss);
            decimal? expectancy = CompletedTrades == 0
                ? null
                : (_grossProfit + _grossLoss) / CompletedTrades;
            TimeSpan? averageHoldingTime = CompletedTrades == 0
                ? null
                : TimeSpan.FromTicks(_totalHoldingTime.Ticks / CompletedTrades);
            return new BacktestExecutionReport(
                _initialBalance,
                Cash,
                position.OpenQuantity,
                netLiquidation,
                grossReturnPercent,
                returnPercent,
                position.RealizedPnl,
                _grossProfit,
                Math.Abs(_grossLoss),
                expectancy,
                TotalFees,
                SpreadCost,
                SlippageCost,
                _maximumDrawdownPercent,
                FillCount,
                CompletedTrades,
                WinningTrades,
                winRate,
                profitFactor,
                averageHoldingTime,
                Target != ExecutionTarget.None,
                FirstFillAt,
                LastFillAt);
        }
    }
}
