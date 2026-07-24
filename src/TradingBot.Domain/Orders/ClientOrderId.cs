namespace TradingBot.Domain.Orders;

public readonly record struct ClientOrderId
{
    private const int MaximumLength = 64;

    private ClientOrderId(string value) => Value = value;

    public string Value { get; }

    public static ClientOrderId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();

        if (value.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Client order id cannot exceed {MaximumLength} characters.");
        }

        return new ClientOrderId(value);
    }

    public override string ToString() => Value;
}
