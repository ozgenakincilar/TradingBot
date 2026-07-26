using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Strategies;

internal static class BacktestInstrumentQuantization
{
    public static decimal NormalizePrice(
        BacktestExecutionPolicy policy,
        OrderSide side,
        decimal price)
    {
        if (policy.InstrumentRules is not { } instrument)
        {
            return price;
        }

        var value = Price.From(price);
        return side == OrderSide.Buy
            ? instrument.NormalizePriceUp(value).Value
            : instrument.NormalizePrice(value).Value;
    }

    public static decimal NormalizeQuantity(BacktestExecutionPolicy policy, decimal quantity)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        if (policy.InstrumentRules is not { } instrument)
        {
            return quantity;
        }

        return quantity < instrument.QuantityStepSize
            ? 0m
            : instrument.NormalizeQuantity(Quantity.From(quantity)).Value;
    }

    public static bool IsTradable(
        BacktestExecutionPolicy policy,
        decimal price,
        decimal quantity)
    {
        return policy.InstrumentRules is not { } instrument ||
            (quantity >= instrument.MinimumQuantity &&
             Multiply(price, quantity) >= instrument.MinimumNotional);
    }

    public static PaperFill? NormalizeFill(
        BacktestExecutionPolicy policy,
        PaperFill fill,
        OrderSide side)
    {
        if (policy.InstrumentRules is null)
        {
            return fill;
        }

        var quantity = NormalizeQuantity(policy, fill.Quantity.Value);
        if (quantity <= 0m)
        {
            return null;
        }

        var price = NormalizePrice(policy, side, fill.Price.Value);
        var fee = Multiply(
            Multiply(price, quantity),
            policy.PaperExecution.CommissionRate.Fraction);
        return fill with
        {
            Quantity = Quantity.From(quantity),
            Price = Price.From(price),
            QuoteFee = Money.Create(fee, policy.QuoteAsset.Value)
        };
    }

    private static decimal Multiply(decimal left, decimal right)
    {
        try
        {
            return checked(left * right);
        }
        catch (OverflowException)
        {
            throw new DomainRuleViolationException(
                "Backtest instrument quantization exceeded decimal bounds.");
        }
    }
}
