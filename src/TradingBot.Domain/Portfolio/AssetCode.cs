namespace TradingBot.Domain.Portfolio;

public readonly record struct AssetCode
{
    private AssetCode(string value) => Value = value;

    public string Value { get; }

    public static AssetCode Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim().ToUpperInvariant();

        if (value.Length is < 2 or > 12 || !value.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException(
                "Asset code must contain 2-12 ASCII letters or digits.",
                nameof(value));
        }

        return new AssetCode(value);
    }

    public override string ToString() => Value;
}
