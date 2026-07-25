using TradingBot.Domain.Common;

namespace TradingBot.Domain.MarketData;

public readonly record struct Timeframe
{
    private Timeframe(TimeSpan duration)
    {
        Duration = duration;
    }

    public TimeSpan Duration { get; }

    public static Timeframe Create(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1) ||
            duration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new DomainRuleViolationException(
                "Timeframe must be a positive whole-second duration.");
        }

        return new Timeframe(duration);
    }

    public bool IsBoundary(DateTimeOffset timestamp)
    {
        if (this == default)
        {
            throw new DomainRuleViolationException("Timeframe is required.");
        }

        EnsureUtc(timestamp, nameof(timestamp));
        return (timestamp.UtcDateTime.Ticks - DateTimeOffset.UnixEpoch.UtcDateTime.Ticks) %
            Duration.Ticks == 0;
    }

    public DateTimeOffset GetBoundaryAtOrBefore(DateTimeOffset timestamp)
    {
        if (this == default)
        {
            throw new DomainRuleViolationException("Timeframe is required.");
        }

        EnsureUtc(timestamp, nameof(timestamp));
        var elapsedTicks = timestamp.UtcDateTime.Ticks - DateTimeOffset.UnixEpoch.UtcDateTime.Ticks;
        var remainder = elapsedTicks % Duration.Ticks;
        if (remainder < 0)
        {
            remainder += Duration.Ticks;
        }

        return new DateTimeOffset(timestamp.UtcDateTime.Ticks - remainder, TimeSpan.Zero);
    }

    internal static void EnsureUtc(DateTimeOffset timestamp, string parameterName)
    {
        if (timestamp == default || timestamp.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException($"{parameterName} must be a UTC timestamp.");
        }
    }
}
