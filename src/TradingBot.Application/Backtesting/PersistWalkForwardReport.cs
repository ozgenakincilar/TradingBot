using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;

namespace TradingBot.Application.Backtesting;

public enum WalkForwardPersistenceStatus
{
    Stored = 1,
    AlreadyStored = 2
}

public sealed class PersistWalkForwardReport(
    IWalkForwardResultRepository repository,
    ITradingUnitOfWork unitOfWork)
{
    public async Task<WalkForwardPersistenceStatus> HandleAsync(
        WalkForwardReport report,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (createdAt == default || createdAt.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException(
                "Walk-forward persistence timestamp must be UTC.");
        }

        var status = WalkForwardPersistenceStatus.Stored;
        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var existing = await repository.GetAsync(
                report.RunSha256,
                transactionCancellationToken);
            if (existing is not null)
            {
                if (!StringComparer.Ordinal.Equals(existing.ReportSha256, report.ReportSha256))
                {
                    throw new DomainRuleViolationException(
                        "The same walk-forward run identity produced a conflicting report.");
                }

                status = WalkForwardPersistenceStatus.AlreadyStored;
                return;
            }

            repository.Add(report, createdAt);
        }, cancellationToken);
        return status;
    }
}
