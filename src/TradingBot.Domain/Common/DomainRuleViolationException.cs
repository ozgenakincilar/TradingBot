namespace TradingBot.Domain.Common;

public sealed class DomainRuleViolationException(string message) : InvalidOperationException(message);
