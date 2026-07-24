namespace TradingBot.Domain.Orders;

public readonly record struct OrderId
{
    private OrderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OrderId New() => new(Guid.NewGuid());

    public static OrderId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Order id cannot be empty.", nameof(value));
        }

        return new OrderId(value);
    }
}
