using TradingBot.Application.Backtesting;

namespace TradingBot.Application.Abstractions.Persistence;

public sealed record StoredWalkForwardResult(string RunSha256, string ReportSha256);

public interface IWalkForwardResultRepository
{
    Task<StoredWalkForwardResult?> GetAsync(
        string runSha256,
        CancellationToken cancellationToken);

    void Add(WalkForwardReport report, DateTimeOffset createdAt);
}
