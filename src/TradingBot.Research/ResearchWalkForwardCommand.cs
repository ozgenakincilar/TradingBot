using System.Globalization;
using TradingBot.Application.Backtesting;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;
using TradingBot.Domain.MarketData;
using TradingBot.Domain.Portfolio;
using TradingBot.Domain.Strategies;
using TradingBot.Infrastructure.Backtesting;

namespace TradingBot.Research;

public sealed record ResearchWalkForwardRequest(
    StrategyDefinition Definition,
    BacktestExecutionPolicy ExecutionPolicy,
    WalkForwardSchedule Schedule,
    CsvHistoricalCandleDatasetFactory DatasetFactory,
    int RandomSeed);

public static class ResearchWalkForwardCommand
{
    private static readonly Timeframe SignalTimeframe =
        Timeframe.Create(TimeSpan.FromMinutes(15));
    private static readonly Timeframe TrendTimeframe =
        Timeframe.Create(TimeSpan.FromHours(1));
    private static readonly IReadOnlySet<string> AllowedOptions = new HashSet<string>(
        [
            "--instrument", "--signal", "--signal-source", "--trend", "--trend-source",
            "--from", "--to", "--training-days", "--validation-days", "--oos-days",
            "--mode", "--seed"
        ],
        StringComparer.Ordinal);

    public static ResearchWalkForwardRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 25 ||
            !string.Equals(arguments[0], "run-walk-forward", StringComparison.Ordinal))
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
            !string.Equals(values["--instrument"], "BTC-USDT", StringComparison.Ordinal) ||
            !TryUtc(values["--from"], out var from) ||
            !TryUtc(values["--to"], out var to) ||
            !TryPositiveDays(values["--training-days"], out var training) ||
            !TryPositiveDays(values["--validation-days"], out var validation) ||
            !TryPositiveDays(values["--oos-days"], out var outOfSample) ||
            !int.TryParse(values["--seed"], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var seed) ||
            !IsCsv(values["--signal"]) || !IsCsv(values["--trend"]))
        {
            throw InvalidCommand();
        }

        var mode = values["--mode"] switch
        {
            "rolling" => WalkForwardTrainingMode.Rolling,
            "expanding" => WalkForwardTrainingMode.Expanding,
            _ => throw InvalidCommand()
        };

        try
        {
            var instrument = InstrumentId.Create("OKX", "BTC-USDT");
            var definition = StrategyDefinition.Create(
                "btc-usdt-long-flat-baseline",
                1,
                instrument,
                SignalTimeframe,
                TrendTimeframe,
                signalEmaPeriod: 20,
                trendEmaPeriod: 200,
                maximumSignalCandleMovePercent: 2m,
                minimumSignalWarmupCandles: 200,
                minimumTrendWarmupCandles: 200);
            var policy = new BacktestExecutionPolicy(
                InitialQuoteBalance: 1_000m,
                AssetCode.Create("BTC"),
                AssetCode.Create("USDT"),
                Percentage.FromPercent(10m),
                SyntheticSpreadBasisPoints: 20m,
                new PaperExecutionPolicy(
                    TimeSpan.FromMilliseconds(100),
                    Percentage.FromPercent(0.1m),
                    SlippageBasisPoints: 10m,
                    Percentage.FromPercent(5m)));
            var schedule = WalkForwardSchedule.Create(
                from,
                to,
                training,
                validation,
                outOfSample,
                mode,
                SignalTimeframe,
                TrendTimeframe);
            var factory = new CsvHistoricalCandleDatasetFactory(
            [
                new CsvHistoricalCandleDatasetRegistration(
                    instrument,
                    SignalTimeframe,
                    values["--signal"],
                    values["--signal-source"],
                    to),
                new CsvHistoricalCandleDatasetRegistration(
                    instrument,
                    TrendTimeframe,
                    values["--trend"],
                    values["--trend-source"],
                    to)
            ]);
            return new ResearchWalkForwardRequest(definition, policy, schedule, factory, seed);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidCommand();
        }
    }

    public static string Usage =>
        "Usage: run-walk-forward --instrument BTC-USDT --signal <15m.csv> " +
        "--signal-source <id> --trend <1H.csv> --trend-source <id> " +
        "--from <UTC-O> --to <UTC-O> --training-days <n> --validation-days <n> " +
        "--oos-days <n> --mode rolling|expanding --seed <int>";

    private static bool TryUtc(string value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed) && parsed.Offset == TimeSpan.Zero;

    private static bool TryPositiveDays(string value, out TimeSpan duration)
    {
        duration = default;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ||
            days is <= 0 or > 3_650)
        {
            return false;
        }

        duration = TimeSpan.FromDays(days);
        return true;
    }

    private static bool IsCsv(string value) =>
        string.Equals(Path.GetExtension(value), ".csv", StringComparison.OrdinalIgnoreCase);

    private static DomainRuleViolationException InvalidCommand() => new(
        "Research walk-forward command is invalid. " + Usage);
}
