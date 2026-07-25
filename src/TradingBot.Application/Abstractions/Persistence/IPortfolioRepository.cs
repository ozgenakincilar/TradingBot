using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Abstractions.Persistence;

public sealed record SpotExecutionRecord(
    OrderId? OrderId,
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
    Task<SpotExecutionRecord?> GetExecutionAsync(
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

    Task<SpotOrderReservation?> GetReservationAsync(
        OrderId orderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AssetBalance>> GetBalancesAsync(
        string exchange,
        CancellationToken cancellationToken);

    void StoreBalance(string exchange, AssetBalance balance);

    void StorePosition(SpotPosition position);

    void StoreReservation(SpotOrderReservation reservation);

    void AddExecution(SpotExecutionRecord execution);
}
