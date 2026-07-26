using System.Collections.Immutable;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Execution;

public sealed record PaperExecutionPolicy(
    TimeSpan MinimumLatency,
    Percentage CommissionRate,
    decimal SlippageBasisPoints,
    Percentage MaximumLiquidityParticipation)
{
    public void Validate()
    {
        if (MinimumLatency <= TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Paper execution latency must be greater than zero.");
        }

        if (CommissionRate.Fraction > 0.05m)
        {
            throw new DomainRuleViolationException("Paper commission rate cannot exceed 5%. ");
        }

        if (SlippageBasisPoints is < 0m or > 1_000m)
        {
            throw new DomainRuleViolationException("Paper slippage must be between 0 and 1,000 basis points.");
        }

        if (MaximumLiquidityParticipation.Fraction <= 0m)
        {
            throw new DomainRuleViolationException("Liquidity participation must be greater than zero.");
        }
    }
}

public readonly record struct PaperOrderBookLevel(Price Price, decimal Quantity)
{
    public void Validate()
    {
        if (Price.Value <= 0m || Quantity <= 0m)
        {
            throw new DomainRuleViolationException("Paper order-book level is invalid.");
        }
    }
}

public sealed record PaperTopOfBookSnapshot(
    InstrumentId InstrumentId,
    Price BestBid,
    decimal BestBidQuantity,
    Price BestAsk,
    decimal BestAskQuantity,
    DateTimeOffset OccurredAt,
    ImmutableArray<PaperOrderBookLevel> BidDepth = default,
    ImmutableArray<PaperOrderBookLevel> AskDepth = default)
{
    public bool HasDepth => !BidDepth.IsDefault;

    public void Validate()
    {
        if (InstrumentId == default || BestBid.Value > BestAsk.Value ||
            BestBidQuantity < 0m || BestAskQuantity < 0m)
        {
            throw new DomainRuleViolationException("Paper top-of-book snapshot is invalid.");
        }

        if (BidDepth.IsDefault != AskDepth.IsDefault)
        {
            throw new DomainRuleViolationException(
                "Paper order-book depth must include both sides.");
        }

        if (BidDepth.IsDefault)
        {
            return;
        }

        if (BidDepth is not { Length: > 0 and <= 5 } ||
            AskDepth is not { Length: > 0 and <= 5 } ||
            BidDepth[0].Price != BestBid || BidDepth[0].Quantity != BestBidQuantity ||
            AskDepth[0].Price != BestAsk || AskDepth[0].Quantity != BestAskQuantity)
        {
            throw new DomainRuleViolationException("Paper order-book depth is invalid.");
        }

        ValidateLevels(BidDepth, descending: true);
        ValidateLevels(AskDepth, descending: false);
    }

    private static void ValidateLevels(
        ImmutableArray<PaperOrderBookLevel> levels,
        bool descending)
    {
        for (var index = 0; index < levels.Length; index++)
        {
            levels[index].Validate();
            if (index == 0)
            {
                continue;
            }

            var previous = levels[index - 1].Price.Value;
            var current = levels[index].Price.Value;
            if (descending ? previous <= current : previous >= current)
            {
                throw new DomainRuleViolationException(
                    "Paper order-book depth prices must be strictly ordered.");
            }
        }
    }
}

public sealed record PaperExecutionRequest(
    OrderId OrderId,
    InstrumentId InstrumentId,
    AssetCode QuoteAsset,
    OrderSide Side,
    OrderType Type,
    Quantity RemainingQuantity,
    Price? LimitPrice,
    DateTimeOffset SubmittedAt)
{
    public void Validate()
    {
        if (OrderId == default || InstrumentId == default || QuoteAsset == default ||
            Side is not (OrderSide.Buy or OrderSide.Sell) ||
            Type is not (OrderType.Market or OrderType.Limit) ||
            (Type == OrderType.Limit && LimitPrice is null) ||
            (Type == OrderType.Market && LimitPrice is not null))
        {
            throw new DomainRuleViolationException("Paper execution request is invalid.");
        }
    }
}

public enum PaperExecutionStatus
{
    WaitingForLatency = 1,
    WaitingForLimitPrice = 2,
    WaitingForLiquidity = 3,
    Filled = 4
}

public sealed record PaperFill(
    OrderId OrderId,
    Quantity Quantity,
    Price Price,
    Money QuoteFee,
    DateTimeOffset OccurredAt);

public sealed record PaperExecutionResult(
    PaperExecutionStatus Status,
    PaperFill? Fill)
{
    public static PaperExecutionResult Waiting(PaperExecutionStatus status) => new(status, null);

    public static PaperExecutionResult Executed(PaperFill fill) =>
        new(PaperExecutionStatus.Filled, fill);
}

public sealed class PaperExecutionEngine
{
    public PaperExecutionResult Evaluate(
        PaperExecutionPolicy policy,
        PaperExecutionRequest request,
        PaperTopOfBookSnapshot market)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(market);
        policy.Validate();
        request.Validate();
        market.Validate();

        if (request.InstrumentId != market.InstrumentId)
        {
            throw new DomainRuleViolationException("Paper order and market snapshot instruments do not match.");
        }

        if (market.OccurredAt < request.SubmittedAt + policy.MinimumLatency)
        {
            return PaperExecutionResult.Waiting(PaperExecutionStatus.WaitingForLatency);
        }

        if (market.HasDepth)
        {
            return EvaluateDepth(policy, request, market);
        }

        var referencePrice = request.Side == OrderSide.Buy ? market.BestAsk.Value : market.BestBid.Value;
        var slippageFraction = policy.SlippageBasisPoints / 10_000m;
        var executionPrice = request.Side == OrderSide.Buy
            ? referencePrice * (1m + slippageFraction)
            : referencePrice * (1m - slippageFraction);
        if (!IsLimitSatisfied(request, executionPrice))
        {
            return PaperExecutionResult.Waiting(PaperExecutionStatus.WaitingForLimitPrice);
        }

        var topQuantity = request.Side == OrderSide.Buy
            ? market.BestAskQuantity
            : market.BestBidQuantity;
        var availableToStrategy = topQuantity * policy.MaximumLiquidityParticipation.Fraction;
        var fillQuantity = Math.Min(request.RemainingQuantity.Value, availableToStrategy);
        if (fillQuantity <= 0m)
        {
            return PaperExecutionResult.Waiting(PaperExecutionStatus.WaitingForLiquidity);
        }

        var fee = executionPrice * fillQuantity * policy.CommissionRate.Fraction;
        return PaperExecutionResult.Executed(new PaperFill(
            request.OrderId,
            Quantity.From(fillQuantity),
            Price.From(executionPrice),
            Money.Create(fee, request.QuoteAsset.Value),
            market.OccurredAt));
    }

    private static PaperExecutionResult EvaluateDepth(
        PaperExecutionPolicy policy,
        PaperExecutionRequest request,
        PaperTopOfBookSnapshot market)
    {
        var levels = request.Side == OrderSide.Buy ? market.AskDepth : market.BidDepth;
        var remaining = request.RemainingQuantity.Value;
        var totalQuantity = 0m;
        var totalNotional = 0m;
        var limitBlocked = false;
        var slippageFraction = policy.SlippageBasisPoints / 10_000m;

        foreach (var level in levels)
        {
            var executionPrice = request.Side == OrderSide.Buy
                ? level.Price.Value * (1m + slippageFraction)
                : level.Price.Value * (1m - slippageFraction);
            if (!IsLimitSatisfied(request, executionPrice))
            {
                limitBlocked = true;
                break;
            }

            var available = level.Quantity *
                policy.MaximumLiquidityParticipation.Fraction;
            var fillQuantity = Math.Min(remaining, available);
            if (fillQuantity <= 0m)
            {
                continue;
            }

            totalQuantity += fillQuantity;
            totalNotional += executionPrice * fillQuantity;
            remaining -= fillQuantity;
            if (remaining <= 0m)
            {
                break;
            }
        }

        if (totalQuantity <= 0m)
        {
            return PaperExecutionResult.Waiting(limitBlocked
                ? PaperExecutionStatus.WaitingForLimitPrice
                : PaperExecutionStatus.WaitingForLiquidity);
        }

        var volumeWeightedPrice = totalNotional / totalQuantity;
        var fee = totalNotional * policy.CommissionRate.Fraction;
        return PaperExecutionResult.Executed(new PaperFill(
            request.OrderId,
            Quantity.From(totalQuantity),
            Price.From(volumeWeightedPrice),
            Money.Create(fee, request.QuoteAsset.Value),
            market.OccurredAt));
    }

    private static bool IsLimitSatisfied(PaperExecutionRequest request, decimal executionPrice)
    {
        if (request.Type == OrderType.Market)
        {
            return true;
        }

        return request.Side == OrderSide.Buy
            ? executionPrice <= request.LimitPrice!.Value.Value
            : executionPrice >= request.LimitPrice!.Value.Value;
    }
}
