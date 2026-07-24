namespace TradingBot.Domain.Common;

public readonly record struct Percentage
{
    private Percentage(decimal fraction) => Fraction = fraction;

    public decimal Fraction { get; }

    public static Percentage FromFraction(decimal fraction)
    {
        if (fraction is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fraction),
                "Percentage fraction must be between zero and one.");
        }

        return new Percentage(fraction);
    }

    public static Percentage FromPercent(decimal percent) => FromFraction(percent / 100m);
}
