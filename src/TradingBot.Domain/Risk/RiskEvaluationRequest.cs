using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;

namespace TradingBot.Domain.Risk;

public sealed record RiskEvaluationRequest(
    Instrument Instrument,
    OrderSide Side,
    Quantity RequestedQuantity,
    Price EntryPrice,
    Price StopPrice,
    Money AccountEquity,
    Money DailyPnl,
    Money CurrentSymbolExposure,
    Money CurrentGrossExposure,
    int OpenOrderCount,
    bool IsKillSwitchActive,
    bool IsMarketDataFresh)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Instrument);

        if (Side is not (OrderSide.Buy or OrderSide.Sell))
        {
            throw new ArgumentOutOfRangeException(nameof(Side));
        }

        if (AccountEquity.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AccountEquity),
                "Account equity must be greater than zero.");
        }

        AccountEquity.EnsureSameCurrency(DailyPnl);
        AccountEquity.EnsureSameCurrency(CurrentSymbolExposure);
        AccountEquity.EnsureSameCurrency(CurrentGrossExposure);

        if (CurrentSymbolExposure.Amount < 0m || CurrentGrossExposure.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CurrentSymbolExposure),
                "Exposure cannot be negative.");
        }

        if (CurrentSymbolExposure.Amount > CurrentGrossExposure.Amount)
        {
            throw new DomainRuleViolationException(
                "Current symbol exposure cannot exceed current gross exposure.");
        }

        if (OpenOrderCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OpenOrderCount));
        }

        if (EntryPrice == StopPrice)
        {
            throw new DomainRuleViolationException("Entry and stop price cannot be equal.");
        }

        if (Side == OrderSide.Buy && StopPrice.Value >= EntryPrice.Value)
        {
            throw new DomainRuleViolationException("Buy order stop price must be below entry price.");
        }

        if (Side == OrderSide.Sell && StopPrice.Value <= EntryPrice.Value)
        {
            throw new DomainRuleViolationException("Sell order stop price must be above entry price.");
        }
    }
}
