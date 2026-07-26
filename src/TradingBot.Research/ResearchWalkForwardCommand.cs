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

public sealed record ResearchStrategyValidationRequest(
    StrategyDefinition Baseline,
    StrategyDefinition Candidate,
    BacktestExecutionPolicy ExecutionPolicy,
    WalkForwardSchedule Schedule,
    CsvHistoricalCandleDatasetFactory DatasetFactory,
    int RandomSeed);

public sealed record ResearchStrategyLossDiagnosticsRequest(
    StrategyDefinition Definition,
    BacktestExecutionPolicy ExecutionPolicy,
    WalkForwardSchedule Schedule,
    CsvHistoricalCandleDatasetFactory DatasetFactory,
    int RandomSeed);

public sealed record ResearchProfitProtectionValidationRequest(
    StrategyDefinition Baseline,
    StrategyDefinition Candidate,
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
    private static readonly IReadOnlySet<string> RequiredOptions = new HashSet<string>(
        [
            "--instrument", "--signal", "--signal-source", "--trend", "--trend-source",
            "--from", "--to", "--training-days", "--validation-days", "--oos-days",
            "--mode", "--seed"
        ],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> InstrumentRuleOptions = new HashSet<string>(
        ["--tick-size", "--quantity-step", "--minimum-quantity", "--minimum-notional"],
        StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> AllowedOptions = new HashSet<string>(
        RequiredOptions.Concat(InstrumentRuleOptions),
        StringComparer.Ordinal);

    public static ResearchWalkForwardRequest Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (25 or 33) ||
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

        var instrumentRuleCount = InstrumentRuleOptions.Count(values.ContainsKey);
        if (!RequiredOptions.All(values.ContainsKey) ||
            instrumentRuleCount is not (0 or 4) ||
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
            var instrumentRules = instrumentRuleCount == 0
                ? null
                : TradingBot.Domain.Instruments.Instrument.Create(
                    instrument,
                    ParsePositiveDecimal(values["--tick-size"]),
                    ParsePositiveDecimal(values["--quantity-step"]),
                    ParsePositiveDecimal(values["--minimum-quantity"]),
                    ParsePositiveDecimal(values["--minimum-notional"]));
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
                    Percentage.FromPercent(5m)),
                instrumentRules);
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

    public static ResearchStrategyValidationRequest ParseValidation(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (25 or 33) ||
            !string.Equals(arguments[0], "validate-hysteresis-v2", StringComparison.Ordinal))
        {
            throw InvalidValidationCommand();
        }

        var normalized = arguments.ToArray();
        normalized[0] = "run-walk-forward";
        try
        {
            var baseline = Parse(normalized);
            var candidate = StrategyDefinition.Create(
                baseline.Definition.StrategyId,
                2,
                baseline.Definition.InstrumentId,
                baseline.Definition.SignalTimeframe,
                baseline.Definition.TrendTimeframe,
                baseline.Definition.SignalEmaPeriod,
                baseline.Definition.TrendEmaPeriod,
                baseline.Definition.MaximumSignalCandleMovePercent,
                baseline.Definition.MinimumSignalWarmupCandles,
                baseline.Definition.MinimumTrendWarmupCandles,
                signalEmaHysteresisBasisPoints: 30m);
            return new ResearchStrategyValidationRequest(
                baseline.Definition,
                candidate,
                baseline.ExecutionPolicy,
                baseline.Schedule,
                baseline.DatasetFactory,
                baseline.RandomSeed);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidValidationCommand();
        }
    }

    public static string Usage =>
        "Usage: run-walk-forward --instrument BTC-USDT --signal <15m.csv> " +
        "--signal-source <id> --trend <1H.csv> --trend-source <id> " +
        "--from <UTC-O> --to <UTC-O> --training-days <n> --validation-days <n> " +
        "--oos-days <n> --mode rolling|expanding --seed <int> " +
        "[--tick-size <decimal> --quantity-step <decimal> " +
        "--minimum-quantity <decimal> --minimum-notional <decimal>]";

    public static string ValidationUsage => Usage.Replace(
        "run-walk-forward",
        "validate-hysteresis-v2",
        StringComparison.Ordinal);

    public static ResearchStrategyLossDiagnosticsRequest ParseLossDiagnostics(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (25 or 33) ||
            !string.Equals(arguments[0], "diagnose-hysteresis-v2", StringComparison.Ordinal))
        {
            throw InvalidDiagnosticsCommand();
        }

        var normalized = arguments.ToArray();
        normalized[0] = "validate-hysteresis-v2";
        try
        {
            var validation = ParseValidation(normalized);
            return new ResearchStrategyLossDiagnosticsRequest(
                validation.Candidate,
                validation.ExecutionPolicy,
                validation.Schedule,
                validation.DatasetFactory,
                validation.RandomSeed);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidDiagnosticsCommand();
        }
    }

    public static string DiagnosticsUsage => Usage.Replace(
        "run-walk-forward",
        "diagnose-hysteresis-v2",
        StringComparison.Ordinal);

    public static ResearchProfitProtectionValidationRequest ParseProfitProtectionValidation(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is not (25 or 33) ||
            !string.Equals(
                arguments[0],
                "validate-profit-protection-v3",
                StringComparison.Ordinal))
        {
            throw InvalidProfitProtectionCommand();
        }

        var normalized = arguments.ToArray();
        normalized[0] = "validate-hysteresis-v2";
        try
        {
            var validation = ParseValidation(normalized);
            var candidate = StrategyDefinition.Create(
                validation.Candidate.StrategyId,
                3,
                validation.Candidate.InstrumentId,
                validation.Candidate.SignalTimeframe,
                validation.Candidate.TrendTimeframe,
                validation.Candidate.SignalEmaPeriod,
                validation.Candidate.TrendEmaPeriod,
                validation.Candidate.MaximumSignalCandleMovePercent,
                validation.Candidate.MinimumSignalWarmupCandles,
                validation.Candidate.MinimumTrendWarmupCandles,
                signalEmaHysteresisBasisPoints: 30m,
                reentryCooldownCandles: 4,
                profitProtectionActivationBasisPoints: 100m,
                profitProtectionTrailingBasisPoints: 50m);
            return new ResearchProfitProtectionValidationRequest(
                validation.Candidate,
                candidate,
                validation.ExecutionPolicy,
                validation.Schedule,
                validation.DatasetFactory,
                validation.RandomSeed);
        }
        catch (DomainRuleViolationException)
        {
            throw InvalidProfitProtectionCommand();
        }
    }

    public static string ProfitProtectionUsage => Usage.Replace(
        "run-walk-forward",
        "validate-profit-protection-v3",
        StringComparison.Ordinal);

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

    private static decimal ParsePositiveDecimal(string value)
    {
        if (!decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed) || parsed <= 0m)
        {
            throw InvalidCommand();
        }

        return parsed;
    }

    private static DomainRuleViolationException InvalidCommand() => new(
        "Research walk-forward command is invalid. " + Usage);

    private static DomainRuleViolationException InvalidValidationCommand() => new(
        "Research strategy validation command is invalid. " + ValidationUsage);

    private static DomainRuleViolationException InvalidDiagnosticsCommand() => new(
        "Research strategy loss diagnostics command is invalid. " + DiagnosticsUsage);

    private static DomainRuleViolationException InvalidProfitProtectionCommand() => new(
        "Research profit protection validation command is invalid. " +
        ProfitProtectionUsage);
}
