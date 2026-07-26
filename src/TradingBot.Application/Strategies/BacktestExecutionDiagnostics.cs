using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Domain.Common;

namespace TradingBot.Application.Strategies;

public sealed record BacktestDiagnosticsPolicy(int MaximumCompletedTrades = 100_000)
{
    public void Validate()
    {
        if (MaximumCompletedTrades is < 1 or > 100_000)
        {
            throw new DomainRuleViolationException(
                "Backtest diagnostics completed-trade limit must be between 1 and 100,000.");
        }
    }
}

public sealed record BacktestTradeAttribution(
    int TradeNumber,
    DateTimeOffset EntryAt,
    DateTimeOffset ExitAt,
    string EntryReasonCode,
    string ExitReasonCode,
    decimal EntryAverageFillPrice,
    decimal ExitAverageFillPrice,
    decimal Quantity,
    decimal NetPnl,
    decimal EstimatedFees,
    decimal EstimatedSpreadCost,
    decimal EstimatedSlippageCost,
    decimal GrossPnlBeforeEstimatedCosts,
    decimal MaximumFavorableExcursionPercent,
    decimal MaximumAdverseExcursionPercent,
    TimeSpan HoldingTime);

public sealed record BacktestExecutionDiagnosticsReport(
    int SchemaVersion,
    string ReportSha256,
    BacktestExecutionReport Execution,
    IReadOnlyList<BacktestTradeAttribution> Trades,
    decimal? AverageMaximumFavorableExcursionPercent,
    decimal? AverageMaximumAdverseExcursionPercent,
    int FavorableExcursionGivenBackTradeCount)
{
    internal static BacktestExecutionDiagnosticsReport Create(
        BacktestExecutionReport execution,
        IReadOnlyList<BacktestTradeAttribution> trades)
    {
        const int schemaVersion = 1;
        var stableTrades = trades.ToArray();
        decimal? averageMfe = stableTrades.Length == 0
            ? null
            : stableTrades.Average(static trade => trade.MaximumFavorableExcursionPercent);
        decimal? averageMae = stableTrades.Length == 0
            ? null
            : stableTrades.Average(static trade => trade.MaximumAdverseExcursionPercent);
        var givenBack = stableTrades.Count(static trade =>
            trade.MaximumFavorableExcursionPercent > 0m && trade.NetPnl <= 0m);
        var canonical = new CanonicalDiagnostics(
            schemaVersion,
            execution,
            stableTrades,
            averageMfe,
            averageMae,
            givenBack);
        var sha256 = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
        return new BacktestExecutionDiagnosticsReport(
            schemaVersion,
            sha256,
            execution,
            stableTrades,
            averageMfe,
            averageMae,
            givenBack);
    }

    private sealed record CanonicalDiagnostics(
        int SchemaVersion,
        BacktestExecutionReport Execution,
        IReadOnlyList<BacktestTradeAttribution> Trades,
        decimal? AverageMaximumFavorableExcursionPercent,
        decimal? AverageMaximumAdverseExcursionPercent,
        int FavorableExcursionGivenBackTradeCount);
}
