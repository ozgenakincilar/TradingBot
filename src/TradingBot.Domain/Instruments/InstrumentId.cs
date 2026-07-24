namespace TradingBot.Domain.Instruments;

public readonly record struct InstrumentId
{
    private InstrumentId(string exchange, string symbol)
    {
        Exchange = exchange;
        Symbol = symbol;
    }

    public string Exchange { get; }

    public string Symbol { get; }

    public static InstrumentId Create(string exchange, string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchange);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        return new InstrumentId(
            exchange.Trim().ToUpperInvariant(),
            symbol.Trim().ToUpperInvariant());
    }

    public override string ToString() => $"{Exchange}:{Symbol}";
}
