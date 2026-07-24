using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Portfolio;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Persistence.Repositories;

public sealed class PortfolioRepository(TradingBotDbContext context) : IPortfolioRepository
{
    public Task<bool> ExecutionExistsAsync(
        string exchange,
        string exchangeExecutionId,
        CancellationToken cancellationToken) =>
        context.SpotExecutions.AsNoTracking().AnyAsync(
            execution => execution.Exchange == exchange &&
                         execution.ExchangeExecutionId == exchangeExecutionId,
            cancellationToken);

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

    public void AddExecution(SpotExecutionRecord execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        context.SpotExecutions.Add(new SpotExecutionEntity
        {
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
