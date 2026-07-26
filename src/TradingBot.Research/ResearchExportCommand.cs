using System.Globalization;
using TradingBot.Application.Backtesting;
using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;

namespace TradingBot.Research;

public static class ResearchExportCommand
{
    private static readonly IReadOnlySet<string> AllowedOptions = new HashSet<string>(
        ["--instrument", "--timeframe", "--from", "--to", "--source", "--output"],
        StringComparer.Ordinal);

    public static HistoricalCandleExportRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 13 ||
            !string.Equals(arguments[0], "export-candles", StringComparison.Ordinal))
        {
            throw InvalidCommand();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            var option = arguments[index];
            var value = arguments[index + 1];
            if (!AllowedOptions.Contains(option) || string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(option, value))
            {
                throw InvalidCommand();
            }
        }

        if (values.Count != AllowedOptions.Count ||
            !TryUtc(values["--from"], out var from) ||
            !TryUtc(values["--to"], out var to))
        {
            throw InvalidCommand();
        }

        var timeframe = values["--timeframe"] switch
        {
            "15m" => Timeframe.Create(TimeSpan.FromMinutes(15)),
            "1H" => Timeframe.Create(TimeSpan.FromHours(1)),
            _ => throw InvalidCommand()
        };
        InstrumentId instrument;
        try
        {
            if (!values["--instrument"].Contains('-', StringComparison.Ordinal))
            {
                throw InvalidCommand();
            }

            instrument = InstrumentId.Create("OKX", values["--instrument"]);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidCommand();
        }

        return new HistoricalCandleExportRequest(
            instrument,
            timeframe,
            from,
            to,
            values["--source"],
            values["--output"]);
    }

    public static string Usage =>
        "Usage: export-candles --instrument BTC-USDT --timeframe 15m|1H " +
        "--from <UTC-O> --to <UTC-O> --source <id> --output <new.csv>";

    private static bool TryUtc(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed) && parsed.Offset == TimeSpan.Zero;

    private static DomainRuleViolationException InvalidCommand() => new(
        "Research export command is invalid. " + Usage);
}
