using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Strategies;

internal readonly record struct CompletedCandleExecutionReference(
    decimal Close,
    decimal High,
    decimal Low,
    decimal BaseVolume,
    bool IsAvailable)
{
    public static CompletedCandleExecutionReference Create(Candle candle)
    {
        ArgumentNullException.ThrowIfNull(candle);
        return new CompletedCandleExecutionReference(
            candle.Close,
            candle.High,
            candle.Low,
            candle.BaseVolume,
            IsAvailable: true);
    }
}

internal readonly record struct DynamicTwapExecutionRequest(
    InstrumentId InstrumentId,
    AssetCode QuoteAsset,
    OrderSide Side,
    CompletedCandleExecutionReference MarketReference,
    decimal ExecutionReferencePrice,
    decimal RequestedQuantity,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ExecutionStart,
    TimeSpan ExecutionDuration);

internal readonly record struct DynamicTwapExecutionSummary(
    decimal ExecutableQuantity,
    decimal FilledQuantity,
    int FillCount);

internal interface IDynamicTwapFillConsumer
{
    void Accept(PaperTopOfBookSnapshot market, PaperFill fill, OrderSide side);
}

internal sealed class DynamicTwapExecutionModel
{
    private static readonly OrderId BuyOrderId = OrderId.From(
        Guid.Parse("79d98574-b7a2-41b1-8bc0-6536180ff932"));
    private static readonly OrderId SellOrderId = OrderId.From(
        Guid.Parse("e27bc43d-67bd-482e-8f41-9c030ab89fe4"));

    private readonly PaperExecutionEngine _execution = new();

    public DynamicTwapExecutionSummary Execute<TConsumer>(
        BacktestExecutionPolicy policy,
        in VolatilityAdjustedExecutionPolicy dynamicExecution,
        in DynamicTwapExecutionRequest request,
        ref TConsumer consumer)
        where TConsumer : struct, IDynamicTwapFillConsumer
    {
        Validate(policy, request);
        var reference = request.MarketReference;
        if (!reference.IsAvailable || reference.BaseVolume <= 0m ||
            request.RequestedQuantity <= 0m)
        {
            return default;
        }

        var participation = policy.PaperExecution.MaximumLiquidityParticipation.Fraction;
        var candleCapacity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            Checked(reference.BaseVolume * participation));
        var executableQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            Math.Min(request.RequestedQuantity, candleCapacity));
        if (executableQuantity <= 0m)
        {
            return default;
        }

        var maximumExecutionPrice = CalculateMaximumExecutionPrice(
            policy,
            dynamicExecution,
            request.ExecutionReferencePrice,
            request.Side);
        var childCount = DetermineChildOrderCount(
            policy,
            maximumExecutionPrice,
            executableQuantity,
            dynamicExecution.TwapChildOrderCount);
        var remaining = executableQuantity;
        var filled = 0m;
        var fillCount = 0;
        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            var remainingChildren = childCount - childIndex;
            var childQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
                policy,
                remaining / remainingChildren);
            if (childQuantity <= 0m)
            {
                continue;
            }

            var cumulativeQuantity = Checked(filled + childQuantity);
            var input = new ExecutionCostInput(
                reference.Close,
                reference.High,
                reference.Low,
                reference.BaseVolume,
                cumulativeQuantity,
                participation);
            var quote = VolatilityAdjustedExecutionCostModel.CalculateValidated(
                in dynamicExecution,
                in input);
            var occurredAt = CalculateChildOrderTime(
                request.ExecutionStart,
                request.ExecutionDuration,
                policy.PaperExecution.MinimumLatency,
                childIndex,
                childCount);
            var market = CreateMarket(
                request.InstrumentId,
                request.ExecutionReferencePrice,
                childQuantity,
                participation,
                quote.SpreadBasisPoints,
                occurredAt,
                policy);
            var childPolicy = policy.PaperExecution with
            {
                SlippageBasisPoints = quote.SlippageBasisPoints
            };
            var result = _execution.Evaluate(
                childPolicy,
                new PaperExecutionRequest(
                    request.Side == OrderSide.Buy ? BuyOrderId : SellOrderId,
                    request.InstrumentId,
                    request.QuoteAsset,
                    request.Side,
                    OrderType.Market,
                    Quantity.From(childQuantity),
                    null,
                    request.SubmittedAt),
                market);
            if (result.Fill is not { } rawFill ||
                BacktestInstrumentQuantization.NormalizeFill(
                    policy,
                    rawFill,
                    request.Side) is not { } normalizedFill)
            {
                continue;
            }

            consumer.Accept(market, normalizedFill, request.Side);
            filled = Checked(filled + normalizedFill.Quantity.Value);
            remaining = Math.Max(0m, Checked(remaining - normalizedFill.Quantity.Value));
            fillCount = Increment(fillCount);
        }

        return new DynamicTwapExecutionSummary(executableQuantity, filled, fillCount);
    }

    public static decimal CalculateMaximumExecutionPrice(
        BacktestExecutionPolicy policy,
        in VolatilityAdjustedExecutionPolicy dynamicExecution,
        decimal executionReferencePrice,
        OrderSide side)
    {
        var spreadFraction = dynamicExecution.MaximumSpreadBasisPoints / 20_000m;
        var topOfBook = side == OrderSide.Buy
            ? Checked(executionReferencePrice * (1m + spreadFraction))
            : Checked(executionReferencePrice * (1m - spreadFraction));
        return BacktestInstrumentQuantization.NormalizePrice(
            policy,
            side,
            ApplySlippage(topOfBook, side, dynamicExecution.MaximumSlippageBasisPoints));
    }

    public static bool HasTradableEntryRemainder(
        BacktestExecutionPolicy policy,
        in VolatilityAdjustedExecutionPolicy dynamicExecution,
        decimal executionReferencePrice,
        decimal remainingBudget,
        decimal availableCash)
    {
        var allocationBudget = Checked(
            policy.InitialQuoteBalance * policy.QuoteAllocation.Fraction);
        var dustLimit = Math.Max(0.00000001m, Checked(allocationBudget * 0.000001m));
        if (remainingBudget <= dustLimit || availableCash <= dustLimit)
        {
            return false;
        }

        var maximumPrice = CalculateMaximumExecutionPrice(
            policy,
            in dynamicExecution,
            executionReferencePrice,
            OrderSide.Buy);
        var unitCost = Checked(maximumPrice *
            (1m + policy.PaperExecution.CommissionRate.Fraction));
        var remainingQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
            policy,
            Math.Min(remainingBudget, availableCash) / unitCost);
        return BacktestInstrumentQuantization.IsTradable(
            policy,
            maximumPrice,
            remainingQuantity);
    }

    public static decimal ClampQuoteDebit(decimal debit, decimal available)
    {
        if (debit <= available)
        {
            return debit;
        }

        var overrun = Checked(debit - available);
        var tolerance = Math.Max(0.000000000001m, Checked(available * 0.000000000001m));
        if (overrun > tolerance)
        {
            throw new DomainRuleViolationException(
                "Dynamic TWAP buy fill exceeded its available quote budget.");
        }

        return available;
    }

    private static int DetermineChildOrderCount(
        BacktestExecutionPolicy policy,
        decimal expectedPrice,
        decimal executableQuantity,
        int configuredCount)
    {
        for (var count = configuredCount; count >= 2; count--)
        {
            var childQuantity = BacktestInstrumentQuantization.NormalizeQuantity(
                policy,
                executableQuantity / count);
            if (BacktestInstrumentQuantization.IsTradable(
                policy,
                expectedPrice,
                childQuantity))
            {
                return count;
            }
        }

        return 1;
    }

    private static DateTimeOffset CalculateChildOrderTime(
        DateTimeOffset executionStart,
        TimeSpan executionDuration,
        TimeSpan minimumLatency,
        int childIndex,
        int childCount)
    {
        try
        {
            var availableTicks = checked(executionDuration.Ticks - minimumLatency.Ticks);
            var offsetTicks = checked(minimumLatency.Ticks +
                availableTicks * childIndex / childCount);
            return executionStart.AddTicks(offsetTicks);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new DomainRuleViolationException(
                "TWAP child-order time exceeded the supported range.");
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "TWAP child-order time calculation overflowed.");
        }
    }

    private static PaperTopOfBookSnapshot CreateMarket(
        InstrumentId instrumentId,
        decimal executionReferencePrice,
        decimal childQuantity,
        decimal participationFraction,
        decimal spreadBasisPoints,
        DateTimeOffset occurredAt,
        BacktestExecutionPolicy policy)
    {
        var halfSpread = spreadBasisPoints / 20_000m;
        var bid = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Sell,
            Checked(executionReferencePrice * (1m - halfSpread)));
        var ask = BacktestInstrumentQuantization.NormalizePrice(
            policy,
            OrderSide.Buy,
            Checked(executionReferencePrice * (1m + halfSpread)));
        var childLiquidity = Checked(childQuantity / participationFraction);
        return new PaperTopOfBookSnapshot(
            instrumentId,
            Price.From(bid),
            childLiquidity,
            Price.From(ask),
            childLiquidity,
            occurredAt);
    }

    private static decimal ApplySlippage(decimal price, OrderSide side, decimal basisPoints)
    {
        var fraction = basisPoints / 10_000m;
        return Checked(side == OrderSide.Buy
            ? price * (1m + fraction)
            : price * (1m - fraction));
    }

    private static void Validate(
        BacktestExecutionPolicy policy,
        in DynamicTwapExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (request.InstrumentId == default || request.QuoteAsset == default ||
            !Enum.IsDefined(request.Side) ||
            request.ExecutionReferencePrice <= 0m || request.RequestedQuantity < 0m ||
            request.SubmittedAt == default || request.ExecutionStart == default ||
            request.SubmittedAt.Offset != TimeSpan.Zero ||
            request.ExecutionStart.Offset != TimeSpan.Zero ||
            request.SubmittedAt > request.ExecutionStart ||
            request.ExecutionDuration <= policy.PaperExecution.MinimumLatency)
        {
            throw new DomainRuleViolationException("Dynamic TWAP execution request is invalid.");
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
            throw new DomainRuleViolationException(
                "Dynamic TWAP financial calculation exceeded decimal bounds.");
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
            throw new DomainRuleViolationException("Dynamic TWAP fill count overflowed.");
        }
    }
}
