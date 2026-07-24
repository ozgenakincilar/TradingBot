namespace TradingBot.Domain;

public readonly record struct MarketPrice(
    string Symbol,
    decimal Price,
    DateTimeOffset Timestamp)
{
    public static MarketPrice Create(string symbol, decimal price, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Fiyat sıfırdan büyük olmalıdır.");
        }

        return new MarketPrice(symbol.ToUpperInvariant(), price, timestamp);
    }
}
