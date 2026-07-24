using TradingBot.Domain.Common;

namespace TradingBot.Domain.Risk;

public sealed class RiskProfile
{
    private RiskProfile(
        Percentage maximumRiskPerTrade,
        Percentage maximumDailyLoss,
        Money maximumSymbolExposure,
        Money maximumGrossExposure,
        int maximumOpenOrders)
    {
        MaximumRiskPerTrade = maximumRiskPerTrade;
        MaximumDailyLoss = maximumDailyLoss;
        MaximumSymbolExposure = maximumSymbolExposure;
        MaximumGrossExposure = maximumGrossExposure;
        MaximumOpenOrders = maximumOpenOrders;
    }

    public Percentage MaximumRiskPerTrade { get; }

    public Percentage MaximumDailyLoss { get; }

    public Money MaximumSymbolExposure { get; }

    public Money MaximumGrossExposure { get; }

    public int MaximumOpenOrders { get; }

    public static RiskProfile Create(
        Percentage maximumRiskPerTrade,
        Percentage maximumDailyLoss,
        Money maximumSymbolExposure,
        Money maximumGrossExposure,
        int maximumOpenOrders)
    {
        if (maximumRiskPerTrade.Fraction <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRiskPerTrade),
                "Maximum risk per trade must be greater than zero.");
        }

        if (maximumDailyLoss.Fraction <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDailyLoss),
                "Maximum daily loss must be greater than zero.");
        }

        if (maximumSymbolExposure.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSymbolExposure),
                "Maximum symbol exposure must be greater than zero.");
        }

        if (maximumGrossExposure.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumGrossExposure),
                "Maximum gross exposure must be greater than zero.");
        }

        maximumSymbolExposure.EnsureSameCurrency(maximumGrossExposure);

        if (maximumSymbolExposure.Amount > maximumGrossExposure.Amount)
        {
            throw new DomainRuleViolationException(
                "Maximum symbol exposure cannot exceed maximum gross exposure.");
        }

        if (maximumOpenOrders <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOpenOrders),
                "Maximum open orders must be greater than zero.");
        }

        return new RiskProfile(
            maximumRiskPerTrade,
            maximumDailyLoss,
            maximumSymbolExposure,
            maximumGrossExposure,
            maximumOpenOrders);
    }
}
