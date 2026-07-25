using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.Orders;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Reconciliation;

public sealed record ReconciliationBalance(
    AssetCode Asset,
    decimal Total,
    decimal Reserved)
{
    public decimal Available => Total - Reserved;

    public void Validate()
    {
        if (Asset == default || Total < 0m || Reserved < 0m || Reserved > Total)
        {
            throw new DomainRuleViolationException("Reconciliation balance is invalid.");
        }
    }
}

public sealed record ReconciliationOrder(
    ClientOrderId ClientOrderId,
    string? ExchangeOrderId,
    InstrumentId InstrumentId,
    OrderSide Side,
    decimal FilledQuantity)
{
    public void Validate()
    {
        if (ClientOrderId == default || InstrumentId == default ||
            Side is not (OrderSide.Buy or OrderSide.Sell) || FilledQuantity < 0m)
        {
            throw new DomainRuleViolationException("Reconciliation order is invalid.");
        }
    }
}

public sealed record SpotAccountSnapshot(
    string Exchange,
    string SnapshotId,
    bool CanTrade,
    DateTimeOffset OccurredAt,
    IReadOnlyCollection<ReconciliationBalance> Balances,
    IReadOnlyCollection<ReconciliationOrder> OpenOrders);

public sealed record LocalSpotAccountState(
    string Exchange,
    IReadOnlyCollection<ReconciliationBalance> Balances,
    IReadOnlyCollection<ReconciliationOrder> ActiveOrders);

public enum ReconciliationDiscrepancyType
{
    AccountTradingDisabled = 1,
    BalanceMissingLocally = 2,
    BalanceMissingOnExchange = 3,
    BalanceTotalMismatch = 4,
    BalanceReservedMismatch = 5,
    OrderMissingLocally = 6,
    OrderMissingOnExchange = 7,
    ExchangeOrderIdMismatch = 8,
    OrderFilledQuantityMismatch = 9
}

public sealed record ReconciliationDiscrepancy(
    ReconciliationDiscrepancyType Type,
    string Key,
    string Description);

public sealed record SpotReconciliationResult(
    bool IsConsistent,
    bool ShouldHaltTrading,
    IReadOnlyCollection<ReconciliationDiscrepancy> Discrepancies);

public sealed class SpotReconciliationEngine
{
    public SpotReconciliationResult Compare(
        SpotAccountSnapshot exchange,
        LocalSpotAccountState local,
        decimal balanceTolerance = 0m)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        ArgumentNullException.ThrowIfNull(local);
        Validate(exchange, local, balanceTolerance);

        var discrepancies = new List<ReconciliationDiscrepancy>();
        if (!exchange.CanTrade)
        {
            discrepancies.Add(new(
                ReconciliationDiscrepancyType.AccountTradingDisabled,
                exchange.Exchange,
                "Exchange account reports canTrade=false."));
        }

        CompareBalances(exchange.Balances, local.Balances, balanceTolerance, discrepancies);
        CompareOrders(exchange.OpenOrders, local.ActiveOrders, discrepancies);

        return new SpotReconciliationResult(
            discrepancies.Count == 0,
            discrepancies.Count > 0,
            discrepancies);
    }

    private static void CompareBalances(
        IReadOnlyCollection<ReconciliationBalance> exchangeBalances,
        IReadOnlyCollection<ReconciliationBalance> localBalances,
        decimal tolerance,
        ICollection<ReconciliationDiscrepancy> discrepancies)
    {
        var exchangeByAsset = exchangeBalances.ToDictionary(static balance => balance.Asset);
        var localByAsset = localBalances.ToDictionary(static balance => balance.Asset);
        foreach (var asset in exchangeByAsset.Keys.Union(localByAsset.Keys).OrderBy(static asset => asset.Value))
        {
            if (!localByAsset.TryGetValue(asset, out var local))
            {
                if (exchangeByAsset[asset].Total != 0m || exchangeByAsset[asset].Reserved != 0m)
                {
                    discrepancies.Add(new(
                        ReconciliationDiscrepancyType.BalanceMissingLocally,
                        asset.Value,
                        $"Exchange has {asset}, but local state does not."));
                }

                continue;
            }

            if (!exchangeByAsset.TryGetValue(asset, out var remote))
            {
                if (local.Total != 0m || local.Reserved != 0m)
                {
                    discrepancies.Add(new(
                        ReconciliationDiscrepancyType.BalanceMissingOnExchange,
                        asset.Value,
                        $"Local state has {asset}, but exchange snapshot does not."));
                }

                continue;
            }

            if (Math.Abs(remote.Total - local.Total) > tolerance)
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.BalanceTotalMismatch,
                    asset.Value,
                    $"Total mismatch for {asset}: exchange={remote.Total}, local={local.Total}."));
            }

            if (Math.Abs(remote.Reserved - local.Reserved) > tolerance)
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.BalanceReservedMismatch,
                    asset.Value,
                    $"Reserved mismatch for {asset}: exchange={remote.Reserved}, local={local.Reserved}."));
            }
        }
    }

    private static void CompareOrders(
        IReadOnlyCollection<ReconciliationOrder> exchangeOrders,
        IReadOnlyCollection<ReconciliationOrder> localOrders,
        ICollection<ReconciliationDiscrepancy> discrepancies)
    {
        var exchangeByClientId = exchangeOrders.ToDictionary(static order => order.ClientOrderId);
        var localByClientId = localOrders.ToDictionary(static order => order.ClientOrderId);
        foreach (var clientOrderId in exchangeByClientId.Keys.Union(localByClientId.Keys))
        {
            if (!localByClientId.TryGetValue(clientOrderId, out var local))
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.OrderMissingLocally,
                    clientOrderId.Value,
                    "Exchange open order is missing locally."));
                continue;
            }

            if (!exchangeByClientId.TryGetValue(clientOrderId, out var remote))
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.OrderMissingOnExchange,
                    clientOrderId.Value,
                    "Local active order is missing from exchange snapshot."));
                continue;
            }

            if (remote.InstrumentId != local.InstrumentId || remote.Side != local.Side ||
                !string.Equals(remote.ExchangeOrderId, local.ExchangeOrderId, StringComparison.Ordinal))
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.ExchangeOrderIdMismatch,
                    clientOrderId.Value,
                    "Exchange and local order identity do not match."));
            }

            if (remote.FilledQuantity != local.FilledQuantity)
            {
                discrepancies.Add(new(
                    ReconciliationDiscrepancyType.OrderFilledQuantityMismatch,
                    clientOrderId.Value,
                    $"Filled quantity mismatch: exchange={remote.FilledQuantity}, local={local.FilledQuantity}."));
            }
        }
    }

    private static void Validate(
        SpotAccountSnapshot exchange,
        LocalSpotAccountState local,
        decimal tolerance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange.Exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange.SnapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(local.Exchange);
        ArgumentNullException.ThrowIfNull(exchange.Balances);
        ArgumentNullException.ThrowIfNull(exchange.OpenOrders);
        ArgumentNullException.ThrowIfNull(local.Balances);
        ArgumentNullException.ThrowIfNull(local.ActiveOrders);
        if (!string.Equals(exchange.Exchange, local.Exchange, StringComparison.Ordinal) || tolerance < 0m)
        {
            throw new DomainRuleViolationException("Reconciliation exchange or tolerance is invalid.");
        }

        foreach (var balance in exchange.Balances.Concat(local.Balances))
        {
            balance.Validate();
        }

        foreach (var order in exchange.OpenOrders.Concat(local.ActiveOrders))
        {
            order.Validate();
            if (!string.Equals(order.InstrumentId.Exchange, exchange.Exchange, StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException("Reconciliation order belongs to another exchange.");
            }
        }

        if (exchange.Balances.Select(static x => x.Asset).Distinct().Count() != exchange.Balances.Count ||
            local.Balances.Select(static x => x.Asset).Distinct().Count() != local.Balances.Count ||
            exchange.OpenOrders.Select(static x => x.ClientOrderId).Distinct().Count() != exchange.OpenOrders.Count ||
            local.ActiveOrders.Select(static x => x.ClientOrderId).Distinct().Count() != local.ActiveOrders.Count)
        {
            throw new DomainRuleViolationException("Reconciliation snapshot contains duplicate keys.");
        }
    }
}
