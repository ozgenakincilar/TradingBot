using TradingBot.Domain.Common;

namespace TradingBot.Domain.Strategies;

public sealed record StrategyTradeContext
{
    private StrategyTradeContext(
        decimal? entrySignalClose,
        decimal? peakClose,
        int? completedCandlesSinceExit)
    {
        EntrySignalClose = entrySignalClose;
        PeakClose = peakClose;
        CompletedCandlesSinceExit = completedCandlesSinceExit;
    }

    public static StrategyTradeContext None { get; } = new(null, null, null);

    public decimal? EntrySignalClose { get; }

    public decimal? PeakClose { get; }

    public int? CompletedCandlesSinceExit { get; }

    public static StrategyTradeContext Open(decimal entrySignalClose)
    {
        if (entrySignalClose <= 0m)
        {
            throw new DomainRuleViolationException("Strategy entry signal close must be positive.");
        }

        return new StrategyTradeContext(entrySignalClose, entrySignalClose, null);
    }

    public static StrategyTradeContext Closed() => new(null, null, 0);

    public StrategyTradeContext ObserveLongClose(decimal close)
    {
        if (EntrySignalClose is not { } entry || PeakClose is not { } peak ||
            CompletedCandlesSinceExit is not null || close <= 0m)
        {
            throw new DomainRuleViolationException(
                "Strategy long trade context cannot observe this close.");
        }

        return new StrategyTradeContext(entry, Math.Max(peak, close), null);
    }

    public StrategyTradeContext AdvanceFlatCandle(int maximum)
    {
        if (EntrySignalClose is not null || PeakClose is not null ||
            CompletedCandlesSinceExit is not { } completed || maximum < 1)
        {
            return this == None
                ? this
                : throw new DomainRuleViolationException(
                    "Strategy flat trade context cannot advance its cooldown.");
        }

        return new StrategyTradeContext(null, null, Math.Min(maximum, completed + 1));
    }
}
