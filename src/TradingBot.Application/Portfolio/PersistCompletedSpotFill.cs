using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Portfolio;

public sealed record PersistCompletedSpotFillCommand(
    string ExchangeExecutionId,
    InstrumentId InstrumentId,
    AssetCode BaseAsset,
    AssetCode QuoteAsset,
    OrderSide Side,
    Quantity Quantity,
    Price Price,
    Money QuoteFee,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public enum PersistSpotFillResult
{
    Applied = 1,
    AlreadyApplied = 2
}

public sealed class PersistCompletedSpotFill(
    IPortfolioRepository portfolio,
    IAuditRepository audit,
    IOutboxRepository outbox,
    ITradingUnitOfWork unitOfWork,
    IIdGenerator idGenerator)
{
    private const string IntegrationEventType = "portfolio.spot-fill-applied.v1";
    private readonly SpotTradeSettlementService _settlement = new();

    public async Task<PersistSpotFillResult> HandleAsync(
        PersistCompletedSpotFillCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var result = PersistSpotFillResult.Applied;
        await unitOfWork.ExecuteAsync(
            async transactionCancellationToken =>
            {
                var existingExecution = await portfolio.GetExecutionAsync(
                        command.InstrumentId.Exchange,
                        command.ExchangeExecutionId,
                        transactionCancellationToken);
                if (existingExecution is not null)
                {
                    if (existingExecution.OrderId is not null ||
                        existingExecution.InstrumentId != command.InstrumentId ||
                        existingExecution.Side != command.Side ||
                        existingExecution.Quantity != command.Quantity.Value ||
                        existingExecution.Price != command.Price.Value ||
                        existingExecution.QuoteFee != command.QuoteFee.Amount)
                    {
                        throw new DomainRuleViolationException(
                            "Exchange execution id conflicts with a different economic fill.");
                    }

                    result = PersistSpotFillResult.AlreadyApplied;
                    return;
                }

                var quoteBalance = await portfolio.GetBalanceAsync(
                    command.InstrumentId.Exchange,
                    command.QuoteAsset,
                    transactionCancellationToken);
                var baseBalance = await portfolio.GetBalanceAsync(
                    command.InstrumentId.Exchange,
                    command.BaseAsset,
                    transactionCancellationToken);
                var position = await portfolio.GetPositionAsync(
                    command.InstrumentId,
                    transactionCancellationToken);

                var applied = ApplyFill(command, quoteBalance, baseBalance, position);

                portfolio.StoreBalance(command.InstrumentId.Exchange, applied.QuoteBalance);
                portfolio.StoreBalance(command.InstrumentId.Exchange, applied.BaseBalance);
                portfolio.StorePosition(applied.Position);

                var execution = new SpotExecutionRecord(
                    null,
                    command.ExchangeExecutionId,
                    command.InstrumentId,
                    command.Side,
                    command.Quantity.Value,
                    command.Price.Value,
                    command.QuoteFee.Amount,
                    applied.RealizedPnl,
                    command.OccurredAt,
                    command.CorrelationId);
                portfolio.AddExecution(execution);

                var payload = SerializeEvent(command, applied.RealizedPnl);
                audit.Add(new AuditRecord(
                    idGenerator.NewGuid(),
                    command.OccurredAt,
                    "Portfolio",
                    "SpotFillApplied",
                    "SpotExecution",
                    command.ExchangeExecutionId,
                    command.CorrelationId,
                    payload));
                outbox.Add(new OutboxRecord(
                    idGenerator.NewGuid(),
                    command.OccurredAt,
                    IntegrationEventType,
                    command.CorrelationId,
                    payload));
            },
            cancellationToken);

        return result;
    }

    private (AssetBalance QuoteBalance, AssetBalance BaseBalance, SpotPosition Position, decimal RealizedPnl) ApplyFill(
        PersistCompletedSpotFillCommand command,
        AssetBalance? quoteBalance,
        AssetBalance? baseBalance,
        SpotPosition? position)
    {
        if (command.Side == OrderSide.Buy)
        {
            if (quoteBalance is null)
            {
                throw new DomainRuleViolationException("Quote asset balance must exist before a buy settlement.");
            }

            baseBalance ??= CreateEmptyBalance(command.BaseAsset, command.OccurredAt);
            position ??= SpotPosition.Open(
                command.InstrumentId,
                command.BaseAsset,
                command.QuoteAsset,
                command.OccurredAt);
            _settlement.ReserveBuy(
                quoteBalance,
                command.Quantity,
                command.Price,
                command.QuoteFee,
                command.OccurredAt);
            _settlement.SettleBuy(
                quoteBalance,
                baseBalance,
                position,
                command.Quantity,
                command.Price,
                command.QuoteFee,
                command.OccurredAt);
            return (quoteBalance, baseBalance, position, 0m);
        }

        if (baseBalance is null || position is null)
        {
            throw new DomainRuleViolationException("A sell fill requires an existing Spot balance and position.");
        }

        quoteBalance ??= CreateEmptyBalance(command.QuoteAsset, command.OccurredAt);
        _settlement.ReserveSell(baseBalance, position, command.Quantity, command.OccurredAt);
        var realizedPnl = _settlement.SettleSell(
            baseBalance,
            quoteBalance,
            position,
            command.Quantity,
            command.Price,
            command.QuoteFee,
            command.OccurredAt).Amount;
        return (quoteBalance, baseBalance, position, realizedPnl);
    }

    private static AssetBalance CreateEmptyBalance(AssetCode asset, DateTimeOffset occurredAt) =>
        AssetBalance.Create(asset, 0m, 0m, occurredAt);

    private static string SerializeEvent(PersistCompletedSpotFillCommand command, decimal realizedPnl) =>
        JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            command.ExchangeExecutionId,
            command.InstrumentId.Exchange,
            command.InstrumentId.Symbol,
            Side = command.Side.ToString(),
            Quantity = command.Quantity.Value,
            Price = command.Price.Value,
            QuoteFee = command.QuoteFee.Amount,
            command.QuoteFee.Currency,
            RealizedPnl = realizedPnl,
            command.OccurredAt
        });

    private static void Validate(PersistCompletedSpotFillCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ExchangeExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.CorrelationId);

        if (command.ExchangeExecutionId.Length > 128 || command.CorrelationId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Execution or correlation id is too long.");
        }

        if (command.InstrumentId == default || command.BaseAsset == default || command.QuoteAsset == default)
        {
            throw new ArgumentException("Instrument and assets are required.", nameof(command));
        }

        if (command.BaseAsset == command.QuoteAsset ||
            !string.Equals(command.QuoteFee.Currency, command.QuoteAsset.Value, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Spot assets or quote fee currency are inconsistent.");
        }

        if (command.QuoteFee.Amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Fee cannot be negative.");
        }

        if (command.Side is not (OrderSide.Buy or OrderSide.Sell))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Unsupported Spot order side.");
        }
    }
}
