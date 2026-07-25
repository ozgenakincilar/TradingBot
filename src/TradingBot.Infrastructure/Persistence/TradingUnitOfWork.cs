using System.Data;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;

namespace TradingBot.Infrastructure.Persistence;

public sealed class TradingUnitOfWork(TradingBotDbContext context) : ITradingUnitOfWork
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            try
            {
                await operation(cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                throw;
            }
        });
    }
}
