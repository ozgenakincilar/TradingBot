using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using static TradingBot.Application.Portfolio.SpotReservationUseCaseSupport;

namespace TradingBot.Application.Portfolio;

public enum SpotReservationOperationResult
{
    Applied = 1,
    AlreadyApplied = 2,
    AlreadyClosed = 3
}

public sealed record ReserveSpotOrderCommand(
    OrderId OrderId,
    AssetCode BaseAsset,
    AssetCode QuoteAsset,
    Price ReservationPrice,
    Money EstimatedQuoteFee,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public sealed record ApplySpotOrderFillCommand(
    OrderId OrderId,
    string ExchangeExecutionId,
    Quantity Quantity,
    Price Price,
    Money QuoteFee,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public sealed record CancelSpotOrderReservationCommand(
    OrderId OrderId,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public sealed class ReserveSpotOrder(
    IOrderRepository orders,
    IPortfolioRepository portfolio,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private readonly SpotTradeSettlementService _settlement = new();

    public async Task<SpotReservationOperationResult> HandleAsync(
        ReserveSpotOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIds(command.OrderId, command.CorrelationId);
        ValidateAssets(command.BaseAsset, command.QuoteAsset, command.EstimatedQuoteFee);

        var result = SpotReservationOperationResult.Applied;
        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var existingReservation = await portfolio.GetReservationAsync(
                command.OrderId,
                transactionCancellationToken);
            if (existingReservation is not null)
            {
                if (existingReservation.BaseAsset != command.BaseAsset ||
                    existingReservation.QuoteAsset != command.QuoteAsset)
                {
                    throw new DomainRuleViolationException(
                        "Order id conflicts with a reservation for different assets.");
                }

                result = SpotReservationOperationResult.AlreadyApplied;
                return;
            }

            var order = await RequireOrderAsync(orders, command.OrderId, transactionCancellationToken);
            if (order.Status != OrderStatus.RiskApproved)
            {
                throw new DomainRuleViolationException("Only a risk-approved order can reserve Spot funds.");
            }

            AssetBalance changedBalance;
            SpotPosition? changedPosition = null;
            SpotOrderReservation reservation;
            if (order.Side == OrderSide.Buy)
            {
                changedBalance = await RequireBalanceAsync(
                    portfolio,
                    order.InstrumentId.Exchange,
                    command.QuoteAsset,
                    transactionCancellationToken);
                reservation = SpotOrderReservation.ReserveBuy(
                    order.Id,
                    order.InstrumentId,
                    command.BaseAsset,
                    command.QuoteAsset,
                    order.ApprovedQuantity,
                    command.ReservationPrice,
                    command.EstimatedQuoteFee,
                    command.OccurredAt);
                _settlement.ReserveBuy(
                    changedBalance,
                    order.ApprovedQuantity,
                    command.ReservationPrice,
                    command.EstimatedQuoteFee,
                    command.OccurredAt);
            }
            else
            {
                changedBalance = await RequireBalanceAsync(
                    portfolio,
                    order.InstrumentId.Exchange,
                    command.BaseAsset,
                    transactionCancellationToken);
                changedPosition = await portfolio.GetPositionAsync(
                    order.InstrumentId,
                    transactionCancellationToken)
                    ?? throw new DomainRuleViolationException("A sell reservation requires an existing Spot position.");
                reservation = SpotOrderReservation.ReserveSell(
                    order.Id,
                    order.InstrumentId,
                    command.BaseAsset,
                    command.QuoteAsset,
                    order.ApprovedQuantity,
                    command.OccurredAt);
                _settlement.ReserveSell(
                    changedBalance,
                    changedPosition,
                    order.ApprovedQuantity,
                    command.OccurredAt);
            }

            portfolio.StoreBalance(order.InstrumentId.Exchange, changedBalance);
            if (changedPosition is not null)
            {
                portfolio.StorePosition(changedPosition);
            }

            portfolio.StoreReservation(reservation);
            AddAuditAndOutbox(
                audit,
                outbox,
                idGenerator,
                command.OrderId,
                command.OccurredAt,
                command.CorrelationId,
                "SpotOrderReserved",
                "portfolio.spot-order-reserved.v1",
                new { reservation.Side, reservation.ApprovedQuantity, reservation.RemainingReserved });
        }, cancellationToken);

        return result;
    }
}

public sealed class ApplySpotOrderFill(
    IOrderRepository orders,
    IPortfolioRepository portfolio,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private readonly SpotTradeSettlementService _settlement = new();

    public async Task<SpotReservationOperationResult> HandleAsync(
        ApplySpotOrderFillCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIds(command.OrderId, command.CorrelationId, command.ExchangeExecutionId);

        var result = SpotReservationOperationResult.Applied;
        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var order = await RequireOrderAsync(orders, command.OrderId, transactionCancellationToken);
            var existingExecution = await portfolio.GetExecutionAsync(
                    order.InstrumentId.Exchange,
                    command.ExchangeExecutionId,
                    transactionCancellationToken);
            if (existingExecution is not null)
            {
                if (existingExecution.OrderId != order.Id ||
                    existingExecution.InstrumentId != order.InstrumentId ||
                    existingExecution.Side != order.Side ||
                    existingExecution.Quantity != command.Quantity.Value ||
                    existingExecution.Price != command.Price.Value ||
                    existingExecution.QuoteFee != command.QuoteFee.Amount)
                {
                    throw new DomainRuleViolationException(
                        "Exchange execution id conflicts with a different order fill.");
                }

                result = SpotReservationOperationResult.AlreadyApplied;
                return;
            }

            var reservation = await portfolio.GetReservationAsync(command.OrderId, transactionCancellationToken)
                ?? throw new DomainRuleViolationException("Spot order reservation was not found.");
            EnsureReservationMatchesOrder(reservation, order);
            if (reservation.Status != SpotReservationStatus.Active)
            {
                throw new DomainRuleViolationException("A fill cannot consume a closed Spot reservation.");
            }

            var quoteBalance = await portfolio.GetBalanceAsync(
                order.InstrumentId.Exchange,
                reservation.QuoteAsset,
                transactionCancellationToken);
            var baseBalance = await portfolio.GetBalanceAsync(
                order.InstrumentId.Exchange,
                reservation.BaseAsset,
                transactionCancellationToken);
            var position = await portfolio.GetPositionAsync(order.InstrumentId, transactionCancellationToken);
            decimal realizedPnl;

            if (order.Side == OrderSide.Buy)
            {
                quoteBalance = quoteBalance
                    ?? throw new DomainRuleViolationException("Reserved quote balance was not found.");
                baseBalance ??= AssetBalance.Create(reservation.BaseAsset, 0m, 0m, command.OccurredAt);
                position ??= SpotPosition.Open(
                    order.InstrumentId,
                    reservation.BaseAsset,
                    reservation.QuoteAsset,
                    command.OccurredAt);
                var quoteDebit = command.Price.Value * command.Quantity.Value + command.QuoteFee.Amount;
                var release = reservation.ApplyBuyFill(
                    command.Quantity,
                    Money.Create(quoteDebit, reservation.QuoteAsset.Value),
                    command.OccurredAt);
                _settlement.SettleBuy(
                    quoteBalance,
                    baseBalance,
                    position,
                    command.Quantity,
                    command.Price,
                    command.QuoteFee,
                    command.OccurredAt);
                if (release > 0m)
                {
                    quoteBalance.Release(release, command.OccurredAt);
                }

                realizedPnl = 0m;
            }
            else
            {
                baseBalance = baseBalance
                    ?? throw new DomainRuleViolationException("Reserved base balance was not found.");
                position = position
                    ?? throw new DomainRuleViolationException("Reserved Spot position was not found.");
                quoteBalance ??= AssetBalance.Create(reservation.QuoteAsset, 0m, 0m, command.OccurredAt);
                reservation.ApplySellFill(command.Quantity, command.OccurredAt);
                realizedPnl = _settlement.SettleSell(
                    baseBalance,
                    quoteBalance,
                    position,
                    command.Quantity,
                    command.Price,
                    command.QuoteFee,
                    command.OccurredAt).Amount;
            }

            order.ApplyFill(command.Quantity, command.Price, command.OccurredAt);
            orders.Store(order);
            portfolio.StoreBalance(order.InstrumentId.Exchange, quoteBalance);
            portfolio.StoreBalance(order.InstrumentId.Exchange, baseBalance);
            portfolio.StorePosition(position);
            portfolio.StoreReservation(reservation);
            portfolio.AddExecution(new SpotExecutionRecord(
                order.Id,
                command.ExchangeExecutionId,
                order.InstrumentId,
                order.Side,
                command.Quantity.Value,
                command.Price.Value,
                command.QuoteFee.Amount,
                realizedPnl,
                command.OccurredAt,
                command.CorrelationId));
            AddAuditAndOutbox(
                audit,
                outbox,
                idGenerator,
                order.Id,
                command.OccurredAt,
                command.CorrelationId,
                "SpotOrderFillApplied",
                "portfolio.spot-order-fill-applied.v1",
                new
                {
                    command.ExchangeExecutionId,
                    Quantity = command.Quantity.Value,
                    Price = command.Price.Value,
                    RealizedPnl = realizedPnl
                });
        }, cancellationToken);

        return result;
    }
}

public sealed class CancelSpotOrderReservation(
    IOrderRepository orders,
    IPortfolioRepository portfolio,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private readonly SpotTradeSettlementService _settlement = new();

    public async Task<SpotReservationOperationResult> HandleAsync(
        CancelSpotOrderReservationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateIds(command.OrderId, command.CorrelationId);

        var result = SpotReservationOperationResult.Applied;
        await unitOfWork.ExecuteAsync(async transactionCancellationToken =>
        {
            var order = await RequireOrderAsync(orders, command.OrderId, transactionCancellationToken);
            var reservation = await portfolio.GetReservationAsync(command.OrderId, transactionCancellationToken)
                ?? throw new DomainRuleViolationException("Spot order reservation was not found.");
            EnsureReservationMatchesOrder(reservation, order);
            if (reservation.Status != SpotReservationStatus.Active)
            {
                result = SpotReservationOperationResult.AlreadyClosed;
                return;
            }

            if (order.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
            {
                order.RequestCancellation(command.OccurredAt);
            }

            if (order.Status != OrderStatus.CancelPending)
            {
                throw new DomainRuleViolationException("Only an open or cancel-pending order can release its reservation.");
            }

            order.MarkCancelled(command.OccurredAt);
            var release = reservation.Cancel(command.OccurredAt);
            if (reservation.Side == OrderSide.Buy)
            {
                var quoteBalance = await RequireBalanceAsync(
                    portfolio,
                    order.InstrumentId.Exchange,
                    reservation.QuoteAsset,
                    transactionCancellationToken);
                quoteBalance.Release(release, command.OccurredAt);
                portfolio.StoreBalance(order.InstrumentId.Exchange, quoteBalance);
            }
            else
            {
                var baseBalance = await RequireBalanceAsync(
                    portfolio,
                    order.InstrumentId.Exchange,
                    reservation.BaseAsset,
                    transactionCancellationToken);
                var position = await portfolio.GetPositionAsync(order.InstrumentId, transactionCancellationToken)
                    ?? throw new DomainRuleViolationException("Reserved Spot position was not found.");
                _settlement.ReleaseSell(
                    baseBalance,
                    position,
                    Quantity.From(release),
                    command.OccurredAt);
                portfolio.StoreBalance(order.InstrumentId.Exchange, baseBalance);
                portfolio.StorePosition(position);
            }

            orders.Store(order);
            portfolio.StoreReservation(reservation);
            AddAuditAndOutbox(
                audit,
                outbox,
                idGenerator,
                order.Id,
                command.OccurredAt,
                command.CorrelationId,
                "SpotOrderReservationCancelled",
                "portfolio.spot-order-reservation-cancelled.v1",
                new { Released = release, reservation.Side, reservation.FilledQuantity });
        }, cancellationToken);

        return result;
    }
}

file static class SpotReservationUseCaseSupport
{
    public static async Task<Order> RequireOrderAsync(
        IOrderRepository orders,
        OrderId orderId,
        CancellationToken cancellationToken) =>
        await orders.GetAsync(orderId, cancellationToken)
        ?? throw new DomainRuleViolationException("Order was not found.");

    public static async Task<AssetBalance> RequireBalanceAsync(
        IPortfolioRepository portfolio,
        string exchange,
        AssetCode asset,
        CancellationToken cancellationToken) =>
        await portfolio.GetBalanceAsync(exchange, asset, cancellationToken)
        ?? throw new DomainRuleViolationException($"{asset} balance was not found.");

    public static void EnsureReservationMatchesOrder(SpotOrderReservation reservation, Order order)
    {
        if (reservation.OrderId != order.Id ||
            reservation.InstrumentId != order.InstrumentId ||
            reservation.Side != order.Side ||
            reservation.ApprovedQuantity != order.ApprovedQuantity.Value)
        {
            throw new DomainRuleViolationException("Reservation and order state do not match.");
        }
    }

    public static void ValidateAssets(AssetCode baseAsset, AssetCode quoteAsset, Money quoteFee)
    {
        if (baseAsset == default || quoteAsset == default || baseAsset == quoteAsset ||
            !string.Equals(quoteAsset.Value, quoteFee.Currency, StringComparison.Ordinal) ||
            quoteFee.Amount < 0m)
        {
            throw new DomainRuleViolationException("Spot assets or quote fee are invalid.");
        }
    }

    public static void ValidateIds(OrderId orderId, string correlationId, string? executionId = null)
    {
        if (orderId == default)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (correlationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(correlationId));
        }

        if (executionId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
            if (executionId.Length > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(executionId));
            }
        }
    }

    public static void AddAuditAndOutbox(
        IAuditRepository audit,
        IOutboxRepository outbox,
        IIdGenerator idGenerator,
        OrderId orderId,
        DateTimeOffset occurredAt,
        string correlationId,
        string action,
        string messageType,
        object details)
    {
        var payload = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OrderId = orderId.Value,
            OccurredAt = occurredAt,
            Details = details
        });
        audit.Add(new AuditRecord(
            idGenerator.NewGuid(),
            occurredAt,
            "Portfolio",
            action,
            nameof(Order),
            orderId.Value.ToString("D"),
            correlationId,
            payload));
        outbox.Add(new OutboxRecord(
            idGenerator.NewGuid(),
            occurredAt,
            messageType,
            correlationId,
            payload));
    }
}
