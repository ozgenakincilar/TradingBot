using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class PortfolioRepository(TradingBotDbContext context) : IPortfolioRepository
{
    public async Task<SpotExecutionRecord?> GetExecutionAsync(
        string exchange,
        string exchangeExecutionId,
        CancellationToken cancellationToken)
    {
        var entity = await context.SpotExecutions.AsNoTracking().SingleOrDefaultAsync(
            execution => execution.Exchange == exchange &&
                         execution.ExchangeExecutionId == exchangeExecutionId,
            cancellationToken);
        return entity is null
            ? null
            : new SpotExecutionRecord(
                entity.OrderId is null ? null : OrderId.From(entity.OrderId.Value),
                entity.ExchangeExecutionId,
                InstrumentId.Create(entity.Exchange, entity.Symbol),
                (OrderSide)entity.Side,
                entity.Quantity,
                entity.Price,
                entity.QuoteFee,
                entity.RealizedPnl,
                entity.OccurredAt,
                entity.CorrelationId);
    }

    public async Task<AssetBalance?> GetBalanceAsync(
        string exchange,
        AssetCode asset,
        CancellationToken cancellationToken)
    {
        var entity = await context.AssetBalances.FindAsync(
            [exchange, asset.Value],
            cancellationToken);
        return entity is null
            ? null
            : AssetBalance.Restore(asset, entity.Total, entity.Reserved, entity.UpdatedAt);
    }

    public async Task<SpotPosition?> GetPositionAsync(
        InstrumentId instrumentId,
        CancellationToken cancellationToken)
    {
        var entity = await context.SpotPositions.FindAsync(
            [instrumentId.Exchange, instrumentId.Symbol],
            cancellationToken);
        return entity is null
            ? null
            : SpotPosition.Restore(
                instrumentId,
                AssetCode.Create(entity.BaseAsset),
                AssetCode.Create(entity.QuoteAsset),
                entity.OpenQuantity,
                entity.ReservedSellQuantity,
                entity.AverageEntryPrice,
                entity.RealizedPnl,
                entity.UpdatedAt);
    }

    public async Task<SpotOrderReservation?> GetReservationAsync(
        OrderId orderId,
        CancellationToken cancellationToken)
    {
        var entity = await context.SpotOrderReservations.FindAsync([orderId.Value], cancellationToken);
        return entity is null
            ? null
            : SpotOrderReservation.Restore(
                orderId,
                InstrumentId.Create(entity.Exchange, entity.Symbol),
                AssetCode.Create(entity.BaseAsset),
                AssetCode.Create(entity.QuoteAsset),
                (OrderSide)entity.Side,
                entity.ApprovedQuantity,
                entity.FilledQuantity,
                entity.RemainingReserved,
                (SpotReservationStatus)entity.Status,
                entity.CreatedAt,
                entity.UpdatedAt);
    }

    public void StoreBalance(string exchange, AssetBalance balance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentNullException.ThrowIfNull(balance);

        var entity = context.AssetBalances.Local.SingleOrDefault(
            candidate => candidate.Exchange == exchange && candidate.Asset == balance.Asset.Value);
        if (entity is null)
        {
            entity = new AssetBalanceEntity
            {
                Exchange = exchange,
                Asset = balance.Asset.Value
            };
            context.AssetBalances.Add(entity);
        }

        entity.Total = balance.Total;
        entity.Reserved = balance.Reserved;
        entity.UpdatedAt = balance.UpdatedAt;
    }

    public void StorePosition(SpotPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var entity = context.SpotPositions.Local.SingleOrDefault(
            candidate => candidate.Exchange == position.InstrumentId.Exchange &&
                         candidate.Symbol == position.InstrumentId.Symbol);
        if (entity is null)
        {
            entity = new SpotPositionEntity
            {
                Exchange = position.InstrumentId.Exchange,
                Symbol = position.InstrumentId.Symbol,
                BaseAsset = position.BaseAsset.Value,
                QuoteAsset = position.QuoteAsset.Value
            };
            context.SpotPositions.Add(entity);
        }

        entity.OpenQuantity = position.OpenQuantity;
        entity.ReservedSellQuantity = position.ReservedSellQuantity;
        entity.AverageEntryPrice = position.AverageEntryPrice;
        entity.RealizedPnl = position.RealizedPnl;
        entity.UpdatedAt = position.UpdatedAt;
    }

    public void StoreReservation(SpotOrderReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        var entity = context.SpotOrderReservations.Local.SingleOrDefault(
            candidate => candidate.OrderId == reservation.OrderId.Value);
        if (entity is null)
        {
            entity = new SpotOrderReservationEntity
            {
                OrderId = reservation.OrderId.Value,
                Exchange = reservation.InstrumentId.Exchange,
                Symbol = reservation.InstrumentId.Symbol,
                BaseAsset = reservation.BaseAsset.Value,
                QuoteAsset = reservation.QuoteAsset.Value,
                Side = (byte)reservation.Side,
                CreatedAt = reservation.CreatedAt
            };
            context.SpotOrderReservations.Add(entity);
        }

        entity.ApprovedQuantity = reservation.ApprovedQuantity;
        entity.FilledQuantity = reservation.FilledQuantity;
        entity.RemainingReserved = reservation.RemainingReserved;
        entity.Status = (byte)reservation.Status;
        entity.UpdatedAt = reservation.UpdatedAt;
    }

    public void AddExecution(SpotExecutionRecord execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        context.SpotExecutions.Add(new SpotExecutionEntity
        {
            OrderId = execution.OrderId?.Value,
            Exchange = execution.InstrumentId.Exchange,
            ExchangeExecutionId = execution.ExchangeExecutionId,
            Symbol = execution.InstrumentId.Symbol,
            Side = (byte)execution.Side,
            Quantity = execution.Quantity,
            Price = execution.Price,
            QuoteFee = execution.QuoteFee,
            RealizedPnl = execution.RealizedPnl,
            OccurredAt = execution.OccurredAt,
            CorrelationId = execution.CorrelationId
        });
    }
}
