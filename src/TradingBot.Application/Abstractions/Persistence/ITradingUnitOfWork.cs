namespace TradingBot.Application.Abstractions.Persistence;

public interface ITradingUnitOfWork
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken);
}
