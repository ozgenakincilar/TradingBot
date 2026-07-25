using System.Text.Json;
using TradingBot.Application.Abstractions;
using TradingBot.Application.Abstractions.Persistence;
using TradingBot.Application.Portfolio;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Application.Tests;

public sealed class PersistCompletedSpotFillTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 14, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.Create("TEST", "BTCUSDT");
    private static readonly AssetCode Btc = AssetCode.Create("BTC");
    private static readonly AssetCode Usdt = AssetCode.Create("USDT");

    [Fact]
    public async Task HandleAsync_BuyFillUpdatesBalancesPositionLedgerAuditAndOutboxAtomically()
    {
        var store = new RecordingStore();
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 1_000m, 0m, Now));
        var handler = CreateHandler(store);

        var result = await handler.HandleAsync(
            CreateCommand("fill-buy-1", OrderSide.Buy, 2m, 100m, 1m),
            CancellationToken.None);

        Assert.Equal(PersistSpotFillResult.Applied, result);
        Assert.Equal(799m, store.Balance("TEST", Usdt).Total);
        Assert.Equal(2m, store.Balance("TEST", Btc).Total);
        Assert.Equal(2m, store.Position(Instrument).OpenQuantity);
        Assert.Equal(100.5m, store.Position(Instrument).AverageEntryPrice);
        Assert.Single(store.Executions);
        Assert.Single(store.Audits);
        var outbox = Assert.Single(store.Outbox);
        Assert.Equal("portfolio.spot-fill-applied.v1", outbox.MessageType);

        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.Equal("fill-buy-1", payload.RootElement.GetProperty("ExchangeExecutionId").GetString());
    }

    [Fact]
    public async Task HandleAsync_SellFillCalculatesNetRealizedPnl()
    {
        var store = new RecordingStore();
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 0m, 0m, Now));
        store.StoreBalance("TEST", AssetBalance.Create(Btc, 2m, 0m, Now));
        store.StorePosition(SpotPosition.Restore(
            Instrument,
            Btc,
            Usdt,
            2m,
            0m,
            100m,
            0m,
            Now));

        var result = await CreateHandler(store).HandleAsync(
            CreateCommand("fill-sell-1", OrderSide.Sell, 1m, 120m, 1m),
            CancellationToken.None);

        Assert.Equal(PersistSpotFillResult.Applied, result);
        Assert.Equal(1m, store.Balance("TEST", Btc).Total);
        Assert.Equal(119m, store.Balance("TEST", Usdt).Total);
        Assert.Equal(19m, store.Position(Instrument).RealizedPnl);
        Assert.Equal(19m, Assert.Single(store.Executions).RealizedPnl);
    }

    [Fact]
    public async Task HandleAsync_SellFillCreatesMissingQuoteBalance()
    {
        var store = new RecordingStore();
        store.StoreBalance("TEST", AssetBalance.Create(Btc, 1m, 0m, Now));
        store.StorePosition(SpotPosition.Restore(
            Instrument,
            Btc,
            Usdt,
            1m,
            0m,
            90m,
            0m,
            Now));

        await CreateHandler(store).HandleAsync(
            CreateCommand("fill-sell-no-quote", OrderSide.Sell, 1m, 100m, 1m),
            CancellationToken.None);

        Assert.Equal(99m, store.Balance("TEST", Usdt).Total);
        Assert.Equal(9m, store.Position(Instrument).RealizedPnl);
    }

    [Fact]
    public async Task HandleAsync_DuplicateExecutionIsIdempotent()
    {
        var store = new RecordingStore();
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 1_000m, 0m, Now));
        var handler = CreateHandler(store);
        var command = CreateCommand("fill-duplicate", OrderSide.Buy, 1m, 100m, 1m);

        Assert.Equal(PersistSpotFillResult.Applied, await handler.HandleAsync(command, CancellationToken.None));
        Assert.Equal(
            PersistSpotFillResult.AlreadyApplied,
            await handler.HandleAsync(command, CancellationToken.None));

        Assert.Equal(899m, store.Balance("TEST", Usdt).Total);
        Assert.Single(store.Executions);
        Assert.Single(store.Audits);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task HandleAsync_InsufficientQuoteBalanceCreatesNoRecords()
    {
        var store = new RecordingStore();
        store.StoreBalance("TEST", AssetBalance.Create(Usdt, 50m, 0m, Now));

        var action = () => CreateHandler(store).HandleAsync(
            CreateCommand("fill-rejected", OrderSide.Buy, 1m, 100m, 1m),
            CancellationToken.None);

        await Assert.ThrowsAsync<DomainRuleViolationException>(action);
        Assert.Equal(50m, store.Balance("TEST", Usdt).Total);
        Assert.Empty(store.Executions);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Outbox);
    }

    private static PersistCompletedSpotFill CreateHandler(RecordingStore store) =>
        new(store, store, store, store, new SequentialIdGenerator());

    private static PersistCompletedSpotFillCommand CreateCommand(
        string executionId,
        OrderSide side,
        decimal quantity,
        decimal price,
        decimal fee) =>
        new(
            executionId,
            Instrument,
            Btc,
            Usdt,
            side,
            Quantity.From(quantity),
            Price.From(price),
            Money.Create(fee, "USDT"),
            Now.AddSeconds(1),
            $"correlation-{executionId}");

    private sealed class RecordingStore :
        IPortfolioRepository,
        IAuditRepository,
        IOutboxRepository,
        ITradingUnitOfWork
    {
        private readonly Dictionary<(string Exchange, string Asset), AssetBalance> _balances = [];
        private readonly Dictionary<InstrumentId, SpotPosition> _positions = [];
        private readonly Dictionary<OrderId, SpotOrderReservation> _reservations = [];

        public List<SpotExecutionRecord> Executions { get; } = [];
        public List<AuditRecord> Audits { get; } = [];
        public List<OutboxRecord> Outbox { get; } = [];

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

        public void StorePosition(SpotPosition position) =>
            _positions[position.InstrumentId] = position;

        public void StoreReservation(SpotOrderReservation reservation) =>
            _reservations[reservation.OrderId] = reservation;

        public void AddExecution(SpotExecutionRecord execution) => Executions.Add(execution);

        public void Add(AuditRecord record) => Audits.Add(record);

        public void Add(OutboxRecord record) => Outbox.Add(record);

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public AssetBalance Balance(string exchange, AssetCode asset) =>
            _balances[(exchange, asset.Value)];

        public SpotPosition Position(InstrumentId instrumentId) => _positions[instrumentId];
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private int _value;

        public Guid NewGuid()
        {
            _value++;
            Span<byte> bytes = stackalloc byte[16];
            BitConverter.TryWriteBytes(bytes, _value);
            return new Guid(bytes);
        }
    }
}
