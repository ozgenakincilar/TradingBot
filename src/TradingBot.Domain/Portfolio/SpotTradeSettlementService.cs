using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Portfolio;

public sealed class SpotTradeSettlementService
{
    public void ReserveBuy(
        AssetBalance quoteBalance,
        Quantity quantity,
        Price price,
        Money estimatedQuoteFee,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(quoteBalance);
        quoteBalance.EnsureEventTime(occurredAt);
        EnsureBalanceCurrency(quoteBalance, estimatedQuoteFee.Currency);
        EnsureNonNegativeFee(estimatedQuoteFee);
        quoteBalance.Reserve(
            price.Value * quantity.Value + estimatedQuoteFee.Amount,
            occurredAt);
    }

    public void SettleBuy(
        AssetBalance quoteBalance,
        AssetBalance baseBalance,
        SpotPosition position,
        Quantity quantity,
        Price price,
        Money quoteFee,
        DateTimeOffset occurredAt)
    {
        ValidateAssets(quoteBalance, baseBalance, position);
        quoteBalance.EnsureEventTime(occurredAt);
        baseBalance.EnsureEventTime(occurredAt);
        position.EnsureEventTime(occurredAt);
        EnsureBalanceCurrency(quoteBalance, quoteFee.Currency);
        EnsureNonNegativeFee(quoteFee);

        var quoteDebit = price.Value * quantity.Value + quoteFee.Amount;
        quoteBalance.EnsureCanDebitReserved(quoteDebit);

        position.ApplyBuyFill(quantity, price, quoteFee, occurredAt);
        quoteBalance.DebitReserved(quoteDebit, occurredAt);
        baseBalance.Credit(quantity.Value, occurredAt);
    }

    public void ReleaseBuy(
        AssetBalance quoteBalance,
        Money reservedQuoteAmount,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(quoteBalance);
        EnsureBalanceCurrency(quoteBalance, reservedQuoteAmount.Currency);

        if (reservedQuoteAmount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedQuoteAmount),
                "Released quote amount must be greater than zero.");
        }

        quoteBalance.Release(reservedQuoteAmount.Amount, occurredAt);
    }

    public void ReserveSell(
        AssetBalance baseBalance,
        SpotPosition position,
        Quantity quantity,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(baseBalance);
        ArgumentNullException.ThrowIfNull(position);

        if (baseBalance.Asset != position.BaseAsset)
        {
            throw new DomainRuleViolationException("Base balance does not match the position base asset.");
        }

        baseBalance.EnsureEventTime(occurredAt);
        position.EnsureEventTime(occurredAt);
        baseBalance.EnsureCanReserve(quantity.Value);
        position.EnsureCanReserveForSell(quantity);
        baseBalance.Reserve(quantity.Value, occurredAt);
        position.ReserveForSell(quantity, occurredAt);
    }

    public void ReleaseSell(
        AssetBalance baseBalance,
        SpotPosition position,
        Quantity quantity,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(baseBalance);
        ArgumentNullException.ThrowIfNull(position);

        if (baseBalance.Asset != position.BaseAsset)
        {
            throw new DomainRuleViolationException("Base balance does not match the position base asset.");
        }

        baseBalance.EnsureEventTime(occurredAt);
        position.EnsureEventTime(occurredAt);
        baseBalance.EnsureCanDebitReserved(quantity.Value);

        if (quantity.Value > position.ReservedSellQuantity)
        {
            throw new DomainRuleViolationException(
                "Cannot release more quantity than the position has reserved.");
        }

        baseBalance.Release(quantity.Value, occurredAt);
        position.ReleaseSellReservation(quantity, occurredAt);
    }

    public Money SettleSell(
        AssetBalance baseBalance,
        AssetBalance quoteBalance,
        SpotPosition position,
        Quantity quantity,
        Price price,
        Money quoteFee,
        DateTimeOffset occurredAt)
    {
        ValidateAssets(quoteBalance, baseBalance, position);
        quoteBalance.EnsureEventTime(occurredAt);
        baseBalance.EnsureEventTime(occurredAt);
        position.EnsureEventTime(occurredAt);
        EnsureBalanceCurrency(quoteBalance, quoteFee.Currency);
        EnsureNonNegativeFee(quoteFee);
        baseBalance.EnsureCanDebitReserved(quantity.Value);

        var netProceeds = price.Value * quantity.Value - quoteFee.Amount;
        if (netProceeds <= 0m)
        {
            throw new DomainRuleViolationException("Sell proceeds must remain positive after fees.");
        }

        var realized = position.ApplySellFill(quantity, price, quoteFee, occurredAt);
        baseBalance.DebitReserved(quantity.Value, occurredAt);
        quoteBalance.Credit(netProceeds, occurredAt);
        return realized;
    }

    private static void ValidateAssets(
        AssetBalance quoteBalance,
        AssetBalance baseBalance,
        SpotPosition position)
    {
        ArgumentNullException.ThrowIfNull(quoteBalance);
        ArgumentNullException.ThrowIfNull(baseBalance);
        ArgumentNullException.ThrowIfNull(position);

        if (quoteBalance.Asset != position.QuoteAsset || baseBalance.Asset != position.BaseAsset)
        {
            throw new DomainRuleViolationException("Settlement balances do not match position assets.");
        }
    }

    private static void EnsureBalanceCurrency(AssetBalance balance, string currency)
    {
        if (!string.Equals(balance.Asset.Value, currency, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"Fee currency {currency} does not match balance asset {balance.Asset}.");
        }
    }

    private static void EnsureNonNegativeFee(Money fee)
    {
        if (fee.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(fee), "Fee cannot be negative.");
        }
    }
}
