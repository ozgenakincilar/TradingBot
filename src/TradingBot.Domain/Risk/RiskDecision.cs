using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.Risk;

public enum RiskDecisionType
{
    Approved = 1,
    Resized = 2,
    Rejected = 3
}

public enum RiskRejectionCode
{
    None = 0,
    KillSwitchActive = 1,
    StaleMarketData = 2,
    DailyLossLimitReached = 3,
    MaximumOpenOrdersReached = 4,
    MaximumSymbolExposureReached = 5,
    MaximumGrossExposureReached = 6,
    BelowTradingMinimum = 7
}

public sealed record RiskDecision(
    RiskDecisionType Type,
    Quantity? ApprovedQuantity,
    RiskRejectionCode RejectionCode,
    string Reason)
{
    public static RiskDecision Approve(Quantity quantity) =>
        new(RiskDecisionType.Approved, quantity, RiskRejectionCode.None, "Approved without resizing.");

    public static RiskDecision Resize(Quantity quantity) =>
        new(RiskDecisionType.Resized, quantity, RiskRejectionCode.None, "Approved quantity was reduced by risk limits.");

    public static RiskDecision Reject(RiskRejectionCode code, string reason) =>
        new(RiskDecisionType.Rejected, null, code, reason);
}
