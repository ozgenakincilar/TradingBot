using TradingBot.Application.Abstractions;

namespace TradingBot.Infrastructure;

public sealed class SystemIdGenerator : IIdGenerator
{
    public Guid NewGuid() => Guid.NewGuid();
}
