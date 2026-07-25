using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Execution;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Tests;

public sealed class ProcessPaperOrderSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 21, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly AssetCode Btc = AssetCode.Create("BTC");
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");

    [Fact]
    public async Task MarketSnapshotCreatesAtomicPartialFillAndDuplicateEventIsIdempotent()
    {
        var setup = await CreateOpenReservedOrderAsync();
        var handler = CreateHandler(setup.Store);
        var command = Command(setup.Order.Id, "market-event-1", Now.AddSeconds(3));

        var first = await handler.HandleAsync(command, CancellationToken.None);
        var duplicate = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(PaperOrderProcessingStatus.FillApplied, first.Status);
        Assert.Equal(PaperOrderProcessingStatus.FillAlreadyApplied, duplicate.Status);
        Assert.Equal(0.5m, first.FillQuantity);
        Assert.Equal(90.09m, first.FillPrice);
        Assert.Equal(0.045045m, first.QuoteFee);
        Assert.Equal(OrderStatus.PartiallyFilled, setup.Order.Status);
        Assert.Equal(0.5m, setup.Order.FilledQuantity);
        Assert.Equal(954.909955m, setup.Store.Balance(Usdt).Total);
        Assert.Equal(0.5m, setup.Store.Balance(Btc).Total);
        Assert.Single(setup.Store.Executions);
    }

    [Fact]
    public async Task SnapshotBeforeLatencyCreatesNoPersistenceEffects()
    {
        var setup = await CreateOpenReservedOrderAsync();

        var outcome = await CreateHandler(setup.Store).HandleAsync(
            Command(setup.Order.Id, "market-event-early", Now.AddSeconds(1).AddMilliseconds(99)),
            CancellationToken.None);

        Assert.Equal(PaperOrderProcessingStatus.WaitingForLatency, outcome.Status);
        Assert.Equal(0m, setup.Order.FilledQuantity);
        Assert.Empty(setup.Store.Executions);
        Assert.Equal(1_000m, setup.Store.Balance(Usdt).Total);
    }

    [Fact]
    public async Task MarketEventCycleDiscoversAndProcessesActiveOrders()
    {
        var setup = await CreateOpenReservedOrderAsync();
        var snapshotCommand = Command(setup.Order.Id, "market-cycle-1", Now.AddSeconds(3));
        var cycle = new ProcessPaperMarketEvent(setup.Store, CreateHandler(setup.Store));

        var outcome = await cycle.HandleAsync(
            new ProcessPaperMarketEventCommand(
                new PaperMarketEvent(
                    snapshotCommand.MarketEventId,
                    1,
                    snapshotCommand.Market.OccurredAt,
                    snapshotCommand.Market),
                snapshotCommand.Policy,
                snapshotCommand.CorrelationId),
            CancellationToken.None);

        var processed = Assert.Single(outcome.Orders);
        Assert.Equal(setup.Order.Id, processed.OrderId);
        Assert.Equal(PaperOrderProcessingStatus.FillApplied, processed.Outcome.Status);
        Assert.Equal(0.5m, setup.Order.FilledQuantity);
        Assert.Single(setup.Store.Executions);
    }

    private static ProcessPaperOrderSnapshotCommand Command(
        OrderId orderId,
        string marketEventId,
        DateTimeOffset occurredAt) =>
        new(
            orderId,
            marketEventId,
            new PaperTopOfBookSnapshot(
                Instrument,
                Price.From(89m),
                2m,
                Price.From(90m),
                2m,
                occurredAt),
            new PaperExecutionPolicy(
                TimeSpan.FromMilliseconds(100),
                Percentage.FromPercent(0.1m),
                10m,
                Percentage.FromPercent(25m)),
            $"correlation-{marketEventId}");

    private static ProcessPaperOrderSnapshot CreateHandler(RecordingStore store)
    {
        var applyFill = new ApplySpotOrderFill(
            store,
            store,
            store,
            store,
            store,
            new SystemIdGenerator());
        return new ProcessPaperOrderSnapshot(store, applyFill);
    }

    private static async Task<(RecordingStore Store, Order Order)> CreateOpenReservedOrderAsync()
    {
        var store = new RecordingStore();
        var order = Order.Create(
            OrderId.New(),
            ClientOrderId.Create($"PAPER-{Guid.NewGuid():N}"),
            Instrument,
            OrderSide.Buy,
            OrderType.Market,
            Quantity.From(2m),
            limitPrice: null,
            Now);
        order.ApproveRisk(Quantity.From(2m), Now);
        store.Add(order);
        store.StoreBalance("PAPER", AssetBalance.Create(Usdt, 1_000m, 0m, Now));
        await new ReserveSpotOrder(
            store,
            store,
            store,
            store,
            store,
            new SystemIdGenerator()).HandleAsync(
                new ReserveSpotOrderCommand(
                    order.Id,
                    Btc,
                    Usdt,
                    Price.From(100m),
                    Money.Create(1m, "USDT"),
                    Now.AddSeconds(1),
                    "paper-reserve"),
                CancellationToken.None);
        order.MarkSubmitting(Now.AddSeconds(2));
        order.MarkAccepted("paper-exchange-order", Now.AddSeconds(2));
        return (store, order);
    }

    private sealed class RecordingStore :
        IOrderRepository,
        IPortfolioRepository,
        IPaperOrderReader,
        IAuditRepository,
        IOutboxRepository,
        ITradingUnitOfWork
    {
        private readonly Dictionary<OrderId, Order> _orders = [];
        private readonly Dictionary<(string Exchange, string Asset), AssetBalance> _balances = [];
        private readonly Dictionary<InstrumentId, SpotPosition> _positions = [];
        private readonly Dictionary<OrderId, SpotOrderReservation> _reservations = [];

        public List<SpotExecutionRecord> Executions { get; } = [];
        public List<AuditRecord> Audits { get; } = [];
        public List<OutboxRecord> Outbox { get; } = [];

        public Task<bool> ExistsAsync(ClientOrderId clientOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(_orders.Values.Any(order => order.ClientOrderId == clientOrderId));

        public Task<Order?> GetAsync(OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult(_orders.GetValueOrDefault(orderId));

        async Task<PaperOrderState?> IPaperOrderReader.GetAsync(
            OrderId orderId,
            CancellationToken cancellationToken)
        {
            var order = await GetAsync(orderId, cancellationToken);
            var reservation = _reservations.GetValueOrDefault(orderId);
            return order is null || reservation is null
                ? null
                : new PaperOrderState(
                    order.Id,
                    order.InstrumentId,
                    reservation.QuoteAsset,
                    order.Side,
                    order.Type,
                    order.Status,
                    order.ApprovedQuantity.Value,
                    order.FilledQuantity,
                    order.LimitPrice,
                    reservation.Status,
                    reservation.CreatedAt);
        }

        public Task<IReadOnlyCollection<OrderId>> GetActiveOrderIdsAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<OrderId>>(
                _orders.Values
                    .Where(order => order.InstrumentId == instrumentId &&
                                    (order.Status is OrderStatus.Open or
                                        OrderStatus.PartiallyFilled or
                                        OrderStatus.CancelPending) &&
                                    _reservations.GetValueOrDefault(order.Id)?.Status ==
                                        SpotReservationStatus.Active)
                    .Select(order => order.Id)
                    .ToArray());

        public Task<IReadOnlyCollection<Order>> GetActiveAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Order>>(_orders.Values.ToArray());

        public void Add(Order order) => _orders[order.Id] = order;

        public void Store(Order order) => _orders[order.Id] = order;

        public Task<SpotExecutionRecord?> GetExecutionAsync(
            string exchange,
            string exchangeExecutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Executions.SingleOrDefault(execution =>
                execution.InstrumentId.Exchange == exchange &&
                execution.ExchangeExecutionId == exchangeExecutionId));

        public Task<AssetBalance?> GetBalanceAsync(
            string exchange,
            AssetCode asset,
            CancellationToken cancellationToken) =>
            Task.FromResult(_balances.GetValueOrDefault((exchange, asset.Value)));

        public Task<SpotPosition?> GetPositionAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_positions.GetValueOrDefault(instrumentId));

        public Task<SpotOrderReservation?> GetReservationAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_reservations.GetValueOrDefault(orderId));

        public Task<IReadOnlyCollection<AssetBalance>> GetBalancesAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AssetBalance>>(
                _balances.Where(pair => pair.Key.Exchange == exchange).Select(pair => pair.Value).ToArray());

        public void StoreBalance(string exchange, AssetBalance balance) =>
            _balances[(exchange, balance.Asset.Value)] = balance;

        public void StorePosition(SpotPosition position) => _positions[position.InstrumentId] = position;

        public void StoreReservation(SpotOrderReservation reservation) =>
            _reservations[reservation.OrderId] = reservation;

        public void AddExecution(SpotExecutionRecord execution) => Executions.Add(execution);

        public void Add(AuditRecord record) => Audits.Add(record);

        public void Add(OutboxRecord record) => Outbox.Add(record);

        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public AssetBalance Balance(AssetCode asset) => _balances[("PAPER", asset.Value)];
    }

    private sealed class SystemIdGenerator : IIdGenerator
    {
        public Guid NewGuid() => Guid.NewGuid();
    }
}
