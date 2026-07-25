using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Reconciliation;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Reconciliation;

namespace TradingBot.Application.Tests;

public sealed class ReconcileSpotAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MismatchPersistsRunAndActivatesTradingHalt()
    {
        var store = CreateStore(localTotal: 100m);

        var outcome = await CreateHandler(store).HandleAsync(
            Command("snapshot-mismatch", remoteTotal: 99m, Now),
            CancellationToken.None);

        Assert.Equal(ReconciliationProcessingStatus.Processed, outcome.Status);
        Assert.False(outcome.IsConsistent);
        Assert.True(outcome.IsTradingHalted);
        Assert.True(store.Safety?.IsHalted);
        Assert.Single(store.Runs);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task DuplicateSnapshotIsIdempotentButConflictingContentIsRejected()
    {
        var store = CreateStore(localTotal: 100m);
        var handler = CreateHandler(store);
        var command = Command("snapshot-duplicate", remoteTotal: 100m, Now);

        await handler.HandleAsync(command, CancellationToken.None);
        var duplicate = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(ReconciliationProcessingStatus.AlreadyProcessed, duplicate.Status);
        Assert.Single(store.Runs);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);

        var conflict = () => handler.HandleAsync(
            Command("snapshot-duplicate", remoteTotal: 99m, Now),
            CancellationToken.None);
        await Assert.ThrowsAsync<TradingBot.Domain.Common.DomainRuleViolationException>(conflict);
    }

    [Fact]
    public async Task CleanSnapshotDoesNotAutomaticallyClearExistingHalt()
    {
        var store = CreateStore(localTotal: 100m);
        await CreateHandler(store).HandleAsync(
            Command("snapshot-bad", remoteTotal: 99m, Now),
            CancellationToken.None);

        var clean = await CreateHandler(store).HandleAsync(
            Command("snapshot-clean", remoteTotal: 100m, Now.AddSeconds(1)),
            CancellationToken.None);

        Assert.True(clean.IsConsistent);
        Assert.True(clean.IsTradingHalted);
        Assert.True(store.Safety?.IsHalted);
    }

    private static RecordingStore CreateStore(decimal localTotal)
    {
        var store = new RecordingStore();
        store.Balances.Add(AssetBalance.Create(
            AssetCode.Create("USDT"),
            localTotal,
            0m,
            Now.AddSeconds(-1)));
        return store;
    }

    private static ReconcileSpotAccountCommand Command(
        string snapshotId,
        decimal remoteTotal,
        DateTimeOffset occurredAt) =>
        new(
            new SpotAccountSnapshot(
                "TEST",
                snapshotId,
                true,
                occurredAt,
                [new ReconciliationBalance(AssetCode.Create("USDT"), remoteTotal, 0m)],
                []),
            0m,
            $"correlation-{snapshotId}");

    private static ReconcileSpotAccount CreateHandler(RecordingStore store) =>
        new(store, store, store, store, store, store, new SystemIdGenerator());

    private sealed class RecordingStore :
        IOrderRepository,
        IPortfolioRepository,
        IReconciliationRepository,
        IAuditRepository,
        IOutboxRepository,
        ITradingUnitOfWork
    {
        public List<AssetBalance> Balances { get; } = [];
        public List<Order> Orders { get; } = [];
        public List<SpotExecutionRecord> Executions { get; } = [];
        public List<ReconciliationRunRecord> Runs { get; } = [];
        public List<AuditRecord> Audits { get; } = [];
        public List<OutboxRecord> Outbox { get; } = [];
        public TradingSafetyStateRecord? Safety { get; private set; }

        public Task<bool> ExistsAsync(ClientOrderId clientOrderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.Any(order => order.ClientOrderId == clientOrderId));

        public Task<Order?> GetAsync(OrderId orderId, CancellationToken cancellationToken) =>
            Task.FromResult(Orders.SingleOrDefault(order => order.Id == orderId));

        public Task<IReadOnlyCollection<Order>> GetActiveAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Order>>(Orders.ToArray());

        public void Add(Order order) => Orders.Add(order);

        public void Store(Order order)
        {
        }

        public Task<SpotExecutionRecord?> GetExecutionAsync(
            string exchange,
            string exchangeExecutionId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Executions.SingleOrDefault(x => x.ExchangeExecutionId == exchangeExecutionId));

        public Task<AssetBalance?> GetBalanceAsync(
            string exchange,
            AssetCode asset,
            CancellationToken cancellationToken) =>
            Task.FromResult(Balances.SingleOrDefault(balance => balance.Asset == asset));

        public Task<SpotPosition?> GetPositionAsync(
            InstrumentId instrumentId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SpotPosition?>(null);

        public Task<SpotOrderReservation?> GetReservationAsync(
            OrderId orderId,
            CancellationToken cancellationToken) =>
            Task.FromResult<SpotOrderReservation?>(null);

        public Task<IReadOnlyCollection<AssetBalance>> GetBalancesAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AssetBalance>>(Balances.ToArray());

        public void StoreBalance(string exchange, AssetBalance balance)
        {
        }

        public void StorePosition(SpotPosition position)
        {
        }

        public void StoreReservation(SpotOrderReservation reservation)
        {
        }

        public void AddExecution(SpotExecutionRecord execution) => Executions.Add(execution);

        public Task<ReconciliationRunRecord?> GetRunAsync(
            string exchange,
            string snapshotId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Runs.SingleOrDefault(run => run.Exchange == exchange && run.SnapshotId == snapshotId));

        public Task<TradingSafetyStateRecord?> GetSafetyStateAsync(
            string exchange,
            CancellationToken cancellationToken) =>
            Task.FromResult(Safety);

        public Task<bool> IsTradingHaltedAsync(string exchange, CancellationToken cancellationToken) =>
            Task.FromResult(Safety?.IsHalted == true);

        public void AddRun(ReconciliationRunRecord run) => Runs.Add(run);

        public void StoreSafetyState(TradingSafetyStateRecord state) => Safety = state;

        public void Add(AuditRecord record) => Audits.Add(record);

        public void Add(OutboxRecord record) => Outbox.Add(record);

        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }

    private sealed class SystemIdGenerator : IIdGenerator
    {
        public Guid NewGuid() => Guid.NewGuid();
    }
}
