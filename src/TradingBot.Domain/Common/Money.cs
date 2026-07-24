namespace TradingBot.Domain.Common;

public readonly record struct Money
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Create(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        currency = currency.Trim().ToUpperInvariant();

        if (currency.Length is < 2 or > 12 || !currency.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("Currency must contain 2-12 ASCII letters or digits.", nameof(currency));
        }

        return new Money(amount, currency);
    }

    public void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException(
                $"Currency mismatch: {Currency} and {other.Currency}.");
        }
    }
}
