using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Tests;

public sealed class SpotOrderReservationUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 16, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("TEST", "BTCUSDT");
    private static readonly AssetCode Btc = AssetCode.Create("BTC");
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");

    [Fact]
    public async Task PartialBuyThenCancelConsumesFillAndReleasesOnlyRemainder()
    {
        var store = new RecordingStore();
        var order = CreateRiskApprovedOrder(quantity: 2m);
        store.Add(order);
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 1_000m, 0m, Now));

        Assert.Equal(
            SpotReservationOperationResult.Applied,
            await CreateReserveHandler(store).HandleAsync(
                ReserveCommand(order.Id, fee: 2m),
                CancellationToken.None));
        Assert.Equal(202m, store.Balance(Usdt).Reserved);

        Open(order);
        Assert.Equal(
            SpotReservationOperationResult.Applied,
            await CreateFillHandler(store).HandleAsync(
                FillCommand(order.Id, "fill-partial", quantity: 1m, price: 90m, fee: 1m, seconds: 4),
                CancellationToken.None));
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
        Assert.Equal(909m, store.Balance(Usdt).Total);
        Assert.Equal(111m, store.Balance(Usdt).Reserved);
        Assert.Equal(1m, store.Balance(Btc).Total);

        Assert.Equal(
            SpotReservationOperationResult.AlreadyApplied,
            await CreateFillHandler(store).HandleAsync(
                FillCommand(order.Id, "fill-partial", quantity: 1m, price: 90m, fee: 1m, seconds: 4),
                CancellationToken.None));

        var conflictingFill = () => CreateFillHandler(store).HandleAsync(
            FillCommand(order.Id, "fill-partial", quantity: 1m, price: 91m, fee: 1m, seconds: 4),
            CancellationToken.None);
        await Assert.ThrowsAsync<DomainRuleViolationException>(conflictingFill);

        Assert.Equal(
            SpotReservationOperationResult.Applied,
            await CreateCancelHandler(store).HandleAsync(
                new CancelSpotOrderReservationCommand(order.Id, Now.AddSeconds(5), "cancel-partial"),
                CancellationToken.None));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(0m, store.Balance(Usdt).Reserved);
        Assert.Equal(SpotReservationStatus.Cancelled, store.Reservation(order.Id).Status);
        Assert.Single(store.Executions);
    }

    [Fact]
    public async Task FillWinsCancelPendingRaceAndLateCancelIsNoOp()
    {
        var store = new RecordingStore();
        var order = CreateRiskApprovedOrder(quantity: 1m);
        store.Add(order);
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 500m, 0m, Now));
        await CreateReserveHandler(store).HandleAsync(ReserveCommand(order.Id, fee: 1m), CancellationToken.None);
        Open(order);
        order.RequestCancellation(Now.AddSeconds(3));

        await CreateFillHandler(store).HandleAsync(
            FillCommand(order.Id, "fill-race", quantity: 1m, price: 100m, fee: 1m, seconds: 4),
            CancellationToken.None);
        var cancelResult = await CreateCancelHandler(store).HandleAsync(
            new CancelSpotOrderReservationCommand(order.Id, Now.AddSeconds(5), "cancel-late"),
            CancellationToken.None);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(SpotReservationStatus.Filled, store.Reservation(order.Id).Status);
        Assert.Equal(SpotReservationOperationResult.AlreadyClosed, cancelResult);
        Assert.Equal(0m, store.Balance(Usdt).Reserved);
    }

    [Fact]
    public async Task CancelWinsRaceAndLateFillIsRejected()
    {
        var store = new RecordingStore();
        var order = CreateRiskApprovedOrder(quantity: 1m);
        store.Add(order);
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 500m, 0m, Now));
        await CreateReserveHandler(store).HandleAsync(ReserveCommand(order.Id, fee: 1m), CancellationToken.None);
        Open(order);
        await CreateCancelHandler(store).HandleAsync(
            new CancelSpotOrderReservationCommand(order.Id, Now.AddSeconds(4), "cancel-first"),
            CancellationToken.None);

        var action = () => CreateFillHandler(store).HandleAsync(
            FillCommand(order.Id, "fill-late", quantity: 1m, price: 100m, fee: 1m, seconds: 5),
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(500m, store.Balance(Usdt).Total);
        Assert.Equal(0m, store.Balance(Usdt).Reserved);
        Assert.Empty(store.Executions);
    }

    private static ReserveSpotOrder CreateReserveHandler(RecordingStore store) =>
        new(store, store, store, store, store, new SequentialIdGenerator());

    private static ApplySpotOrderFill CreateFillHandler(RecordingStore store) =>
        new(store, store, store, store, store, new SequentialIdGenerator());

    private static CancelSpotOrderReservation CreateCancelHandler(RecordingStore store) =>
        new(store, store, store, store, store, new SequentialIdGenerator());

    private static ReserveSpotOrderCommand ReserveCommand(OrderId orderId, decimal fee) =>
        new(orderId, Btc, Usdt, Price.From(100m), Money.Create(fee, "USDT"), Now.AddSeconds(1), "reserve-order");

    private static ApplySpotOrderFillCommand FillCommand(
        OrderId orderId,
        string executionId,
        decimal quantity,
        decimal price,
        decimal fee,
        int seconds) =>
        new(
            orderId,
            executionId,
            Quantity.From(quantity),
            Price.From(price),
            Money.Create(fee, "USDT"),
            Now.AddSeconds(seconds),
            $"correlation-{executionId}");

    private static Order CreateRiskApprovedOrder(decimal quantity)
    {
        var order = Order.Create(
            OrderId.New(),
            ClientOrderId.Create($"BOT-{Guid.NewGuid():N}"),
            Instrument,
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(quantity),
            Price.From(100m),
            Now);
        order.ApproveRisk(Quantity.From(quantity), Now);
        return order;
    }

    private static void Open(Order order)
    {
        order.MarkSubmitting(Now.AddSeconds(2));
        order.MarkAccepted("exchange-order", Now.AddSeconds(2));
    }

    private sealed class RecordingStore :
        IOrderRepository,
        IPortfolioRepository,
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

        public void Add(Order order) => _orders[order.Id] = order;

        public void Store(Order order) => _orders[order.Id] = order;

        public Task<SpotExecutionRecord?> GetExecutionAsync(
            string exchange,
            string exchangeExecutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Executions.SingleOrDefault(
                execution => execution.InstrumentId.Exchange == exchange &&
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

        public AssetBalance Balance(AssetCode asset) => _balances[("TEST", asset.Value)];

        public SpotOrderReservation Reservation(OrderId orderId) => _reservations[orderId];
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        public Guid NewGuid() => Guid.NewGuid();
    }
}
