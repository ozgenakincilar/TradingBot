using System.Collections.Immutable;
using System.Globalization;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Infrastructure.Integrations.Okx;

internal static class OkxOrderBookDepthParser
{
    public static ImmutableArray<PaperOrderBookLevel> Parse(string[][]? rows)
    {
        if (rows is not { Length: > 0 and <= 5 })
        {
            throw new DomainRuleViolationException("OKX order-book depth was invalid.");
        }

        var builder = ImmutableArray.CreateBuilder<PaperOrderBookLevel>(rows.Length);
        foreach (var row in rows)
        {
            if (row.Length < 2 ||
                !TryPositiveDecimal(row[0], out var price) ||
                !TryPositiveDecimal(row[1], out var quantity))
            {
                throw new DomainRuleViolationException("OKX order-book depth was invalid.");
            }

            builder.Add(new PaperOrderBookLevel(Price.From(price), quantity));
        }

        return builder.MoveToImmutable();
    }

    private static bool TryPositiveDecimal(string value, out decimal parsed) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out parsed) && parsed > 0m;
}
