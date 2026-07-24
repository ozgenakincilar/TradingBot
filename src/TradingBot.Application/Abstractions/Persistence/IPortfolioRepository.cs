using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Abstractions.Persistence;

public sealed record SpotExecutionRecord(
    string ExchangeExecutionId,
    InstrumentId InstrumentId,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal QuoteFee,
    decimal RealizedPnl,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public interface IPortfolioRepository
{
    Task<bool> ExecutionExistsAsync(
        string exchange,
        string exchangeExecutionId,
        CancellationToken cancellationToken);

    Task<AssetBalance?> GetBalanceAsync(
        string exchange,
        AssetCode asset,
        CancellationToken cancellationToken);

    Task<SpotPosition?> GetPositionAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken);

    void StoreBalance(string exchange, AssetBalance balance);

    void StorePosition(SpotPosition position);

    void AddExecution(SpotExecutionRecord execution);
}
