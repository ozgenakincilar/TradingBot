namespace TradingBot.Domain.Instruments;

public readonly record struct Quantity
{
    private Quantity(decimal value) => Value = value;

    public decimal Value { get; }

    public static Quantity From(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Quantity must be greater than zero.");
        }

        return new Quantity(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
