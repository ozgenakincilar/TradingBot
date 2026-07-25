using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Risk;

public sealed class RiskEngine
{
    public RiskDecision Evaluate(RiskProfile profile, RiskEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        profile.MaximumSymbolExposure.EnsureSameCurrency(request.AccountEquity);
        profile.MaximumGrossExposure.EnsureSameCurrency(request.AccountEquity);

        if (request.IsKillSwitchActive)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.KillSwitchActive,
                "Kill switch is active.");
        }

        if (!request.IsMarketDataFresh)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.StaleMarketData,
                "Market data is stale or has an unresolved sequence gap.");
        }

        var dailyLoss = Math.Max(0m, -request.DailyPnl.Amount);
        var maximumDailyLossAmount = request.AccountEquity.Amount * profile.MaximumDailyLoss.Fraction;
        if (dailyLoss >= maximumDailyLossAmount)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.DailyLossLimitReached,
                "Maximum daily loss has been reached.");
        }

        if (request.OpenOrderCount >= profile.MaximumOpenOrders)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.MaximumOpenOrdersReached,
                "Maximum open order count has been reached.");
        }

        var symbolCapacity = profile.MaximumSymbolExposure.Amount - request.CurrentSymbolExposure.Amount;
        if (symbolCapacity <= 0m)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.MaximumSymbolExposureReached,
                "Maximum symbol exposure has been reached.");
        }

        var grossCapacity = profile.MaximumGrossExposure.Amount - request.CurrentGrossExposure.Amount;
        if (grossCapacity <= 0m)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.MaximumGrossExposureReached,
                "Maximum gross exposure has been reached.");
        }

        var riskPerUnit = Math.Abs(request.EntryPrice.Value - request.StopPrice.Value);
        var riskCapacity = request.AccountEquity.Amount * profile.MaximumRiskPerTrade.Fraction;
        var riskLimitedQuantity = riskCapacity / riskPerUnit;
        var symbolLimitedQuantity = symbolCapacity / request.EntryPrice.Value;
        var grossLimitedQuantity = grossCapacity / request.EntryPrice.Value;

        var candidate = Math.Min(
            request.RequestedQuantity.Value,
            Math.Min(riskLimitedQuantity, Math.Min(symbolLimitedQuantity, grossLimitedQuantity)));

        if (candidate < request.Instrument.QuantityStepSize)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.BelowTradingMinimum,
                "Risk-adjusted quantity is below the instrument step size.");
        }

        var approvedQuantity = request.Instrument.NormalizeQuantity(Quantity.From(candidate));

        try
        {
            request.Instrument.EnsureTradable(request.EntryPrice, approvedQuantity);
        }
        catch (DomainRuleViolationException exception)
        {
            return RiskDecision.Reject(
                RiskRejectionCode.BelowTradingMinimum,
                exception.Message);
        }

        return approvedQuantity.Value == request.RequestedQuantity.Value
            ? RiskDecision.Approve(approvedQuantity)
            : RiskDecision.Resize(approvedQuantity);
    }
}
