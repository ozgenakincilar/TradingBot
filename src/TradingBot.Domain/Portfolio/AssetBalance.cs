using TradingBot.Domain.Common;

namespace TradingBot.Domain.Portfolio;

public sealed class AssetBalance
{
    private AssetBalance(
        AssetCode asset,
        decimal total,
        decimal reserved,
        DateTimeOffset updatedAt)
    {
        Asset = asset;
        Total = total;
        Reserved = reserved;
        UpdatedAt = updatedAt;
    }

    public AssetCode Asset { get; }

    public decimal Total { get; private set; }

    public decimal Reserved { get; private set; }

    public decimal Available => Total - Reserved;

    public DateTimeOffset UpdatedAt { get; private set; }

    public static AssetBalance Create(
        AssetCode asset,
        decimal total,
        decimal reserved,
        DateTimeOffset updatedAt)
    {
        if (asset == default)
        {
            throw new ArgumentException("Asset is required.", nameof(asset));
        }

        if (total < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "Total balance cannot be negative.");
        }

        if (reserved < 0m || reserved > total)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserved),
                "Reserved balance must be between zero and total balance.");
        }

        return new AssetBalance(asset, total, reserved, updatedAt);
    }

    public static AssetBalance Restore(
        AssetCode asset,
        decimal total,
        decimal reserved,
        DateTimeOffset updatedAt) =>
        Create(asset, total, reserved, updatedAt);

    public void Credit(decimal amount, DateTimeOffset occurredAt)
    {
        EnsurePositive(amount, nameof(amount));
        EnsureEventTime(occurredAt);
        Total += amount;
        UpdatedAt = occurredAt;
    }

    public void Reserve(decimal amount, DateTimeOffset occurredAt)
    {
        EnsureCanReserve(amount);
        EnsureEventTime(occurredAt);
        Reserved += amount;
        UpdatedAt = occurredAt;
    }

    public void Release(decimal amount, DateTimeOffset occurredAt)
    {
        EnsurePositive(amount, nameof(amount));
        EnsureEventTime(occurredAt);

        if (amount > Reserved)
        {
            throw new DomainRuleViolationException(
                $"Cannot release {amount} {Asset}; only {Reserved} is reserved.");
        }

        Reserved -= amount;
        UpdatedAt = occurredAt;
    }

    public void DebitReserved(decimal amount, DateTimeOffset occurredAt)
    {
        EnsureCanDebitReserved(amount);
        EnsureEventTime(occurredAt);
        Reserved -= amount;
        Total -= amount;
        UpdatedAt = occurredAt;
    }

    public void EnsureCanReserve(decimal amount)
    {
        EnsurePositive(amount, nameof(amount));

        if (amount > Available)
        {
            throw new DomainRuleViolationException(
                $"Insufficient available {Asset} balance. Requested {amount}, available {Available}.");
        }
    }

    public void EnsureCanDebitReserved(decimal amount)
    {
        EnsurePositive(amount, nameof(amount));

        if (amount > Reserved)
        {
            throw new DomainRuleViolationException(
                $"Insufficient reserved {Asset} balance. Requested {amount}, reserved {Reserved}.");
        }
    }

    public void EnsureEventTime(DateTimeOffset occurredAt)
    {
        if (occurredAt < UpdatedAt)
        {
            throw new DomainRuleViolationException("Balance events cannot move backwards in time.");
        }
    }

    private static void EnsurePositive(decimal amount, string parameterName)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Amount must be greater than zero.");
        }
    }
}
