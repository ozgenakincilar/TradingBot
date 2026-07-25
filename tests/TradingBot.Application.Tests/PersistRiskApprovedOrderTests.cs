using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Orders;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Risk;

namespace TradingBot.Application.Tests;

public sealed class PersistRiskApprovedOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_StoresOrderRiskAuditAndOutboxInOneUnitOfWork()
    {
        var store = new RecordingStore();
        var handler = CreateHandler(store, new SequentialIdGenerator());
        var order = CreateRiskApprovedOrder();

        var result = await handler.HandleAsync(
            CreateCommand(order, RiskDecision.Approve(order.ApprovedQuantity)),
            CancellationToken.None);

        Assert.Equal(PersistOrderResult.Stored, result);
        Assert.Equal(1, store.UnitOfWorkExecutions);
        Assert.Single(store.Orders);
        Assert.Single(store.RiskDecisions);
        Assert.Single(store.AuditRecords);
        var message = Assert.Single(store.OutboxRecords);
        Assert.Equal("order.risk-approved.v1", message.MessageType);

        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(1, payload.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(order.ClientOrderId.Value, payload.RootElement.GetProperty("ClientOrderId").GetString());
    }

    [Fact]
    public async Task HandleAsync_DuplicateClientOrderIdIsIdempotent()
    {
        var order = CreateRiskApprovedOrder();
        var store = new RecordingStore();
        store.Orders.Add(order);
        var handler = CreateHandler(store, new SequentialIdGenerator());

        var result = await handler.HandleAsync(
            CreateCommand(order, RiskDecision.Approve(order.ApprovedQuantity)),
            CancellationToken.None);

        Assert.Equal(PersistOrderResult.AlreadyExists, result);
        Assert.Single(store.Orders);
        Assert.Empty(store.RiskDecisions);
        Assert.Empty(store.AuditRecords);
        Assert.Empty(store.OutboxRecords);
    }

    [Fact]
    public async Task HandleAsync_RejectsNonApprovedRiskDecisionBeforeTransaction()
    {
        var store = new RecordingStore();
        var handler = CreateHandler(store, new SequentialIdGenerator());
        var order = CreateRiskApprovedOrder();
        var command = CreateCommand(
            order,
            RiskDecision.Reject(RiskRejectionCode.DailyLossLimitReached, "Daily loss limit."));

        var action = () => handler.HandleAsync(command, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, store.UnitOfWorkExecutions);
    }

    [Fact]
    public async Task HandleAsync_RejectsQuantityMismatchBeforeTransaction()
    {
        var store = new RecordingStore();
        var handler = CreateHandler(store, new SequentialIdGenerator());
        var order = CreateRiskApprovedOrder();
        var command = CreateCommand(order, RiskDecision.Resize(Quantity.From(0.25m)));

        var action = () => handler.HandleAsync(command, CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(0, store.UnitOfWorkExecutions);
    }

    [Fact]
    public async Task HandleAsync_UnitOfWorkRollsBackAllStagedRecordsOnFailure()
    {
        var store = new RecordingStore();
        var handler = CreateHandler(store, new FailingIdGenerator(failAtCall: 3));
        var order = CreateRiskApprovedOrder();

        var action = () => handler.HandleAsync(
            CreateCommand(order, RiskDecision.Approve(order.ApprovedQuantity)),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Empty(store.Orders);
        Assert.Empty(store.RiskDecisions);
        Assert.Empty(store.AuditRecords);
        Assert.Empty(store.OutboxRecords);
    }

    [Fact]
    public async Task HandleAsync_ReconciliationHaltBlocksNewEconomicOrder()
    {
        var store = new RecordingStore { IsHalted = true };
        var handler = CreateHandler(store, new SequentialIdGenerator());
        var order = CreateRiskApprovedOrder();

        var action = () => handler.HandleAsync(
            CreateCommand(order, RiskDecision.Approve(order.ApprovedQuantity)),
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Empty(store.Orders);
    }

    private static PersistRiskApprovedOrder CreateHandler(RecordingStore store, IIdGenerator idGenerator) =>
        new(store, store, store, store, store, store, idGenerator);

    private static PersistRiskApprovedOrderCommand CreateCommand(Order order, RiskDecision decision) =>
        new(order, decision, Now.AddSeconds(2), "correlation-001");

    private static Order CreateRiskApprovedOrder()
    {
        var order = Order.Create(
            OrderId.From(Guid.Parse("9d5a3c1b-6285-49fc-93ea-67128be004f4")),
            ClientOrderId.Create("BOT-ATOMIC-0001"),
            InstrumentId.Create("TEST", "BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            Quantity.From(0.5m),
            Price.From(100m),
            Now);
        order.ApproveRisk(Quantity.From(0.5m), Now.AddSeconds(1));
        return order;
    }

    private sealed class RecordingStore :
        IOrderRepository,
        IRiskDecisionRepository,
        IAuditRepository,
        IOutboxRepository,
        IReconciliationRepository,
        ITradingUnitOfWork
    {
        public List<Order> Orders { get; } = [];

        public List<RiskDecision> RiskDecisions { get; } = [];

        public List<AuditRecord> AuditRecords { get; } = [];

        public List<OutboxRecord> OutboxRecords { get; } = [];

        public int UnitOfWorkExecutions { get; private set; }

        public bool IsHalted { get; init; }

        public Task<bool> ExistsAsync(
            ClientOrderId clientOrderId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Orders.Any(order => order.ClientOrderId == clientOrderId));
        }

        public void Add(Order order) => Orders.Add(order);

        public Task<Order?> GetAsync(OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(order => order.Id == orderId));

        public Task<IReadOnlyCollection<Order>> GetActiveAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Order>>([]);

        public void Store(Order order)
        {
        }

        public void Add(Guid id, OrderId orderId, RiskDecision decision, DateTimeOffset occurredAt) =>
            RiskDecisions.Add(decision);

        public void Add(AuditRecord record) => AuditRecords.Add(record);

        public void Add(OutboxRecord record) => OutboxRecords.Add(record);

        public Task<ReconciliationRunRecord?> GetRunAsync(
            string exchange,
            string snapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ReconciliationRunRecord?>(null);

        public Task<TradingSafetyStateRecord?> GetSafetyStateAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<TradingSafetyStateRecord?>(null);

        public Task<bool> IsTradingHaltedAsync(string exchange, CancellationToken cancellationToken) =>
            Task.FromResult(IsHalted);

        public void AddRun(ReconciliationRunRecord run)
        {
        }

        public void StoreSafetyState(TradingSafetyStateRecord state)
        {
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            UnitOfWorkExecutions++;
            var snapshot = (Orders.Count, RiskDecisions.Count, AuditRecords.Count, OutboxRecords.Count);
            try
            {
                await operation(cancellationToken);
            }
            catch
            {
                Orders.RemoveRange(snapshot.Item1, Orders.Count - snapshot.Item1);
                RiskDecisions.RemoveRange(snapshot.Item2, RiskDecisions.Count - snapshot.Item2);
                AuditRecords.RemoveRange(snapshot.Item3, AuditRecords.Count - snapshot.Item3);
                OutboxRecords.RemoveRange(snapshot.Item4, OutboxRecords.Count - snapshot.Item4);
                throw;
            }
        }
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _sequence;

        public Guid NewGuid()
        {
            _sequence++;
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, _sequence);
            return new Guid(bytes);
        }
    }

    private sealed class FailingIdGenerator(int failAtCall) : IIdGenerator
    {
        private int _calls;

        public Guid NewGuid()
        {
            _calls++;
            return _calls == failAtCall
                ? throw new InvalidOperationException("Injected id generation failure.")
                : Guid.NewGuid();
        }
    }
}
