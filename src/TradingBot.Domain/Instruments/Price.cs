namespace TradingBot.Domain.Instruments;

public readonly record struct Price
{
    private Price(decimal value) => Value = value;

    public decimal Value { get; }

    public static Price From(decimal value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Price must be greater than zero.");
        }

        return new Price(value);
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
