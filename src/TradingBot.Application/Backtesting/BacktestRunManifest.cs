using System.Security.Cryptography;
using System.Text.Json;
using TradingBot.Application.Strategies;
using TradingBot.Domain.Common;
using TradingBot.Domain.Strategies;

namespace TradingBot.Application.Backtesting;

public sealed record BacktestRunManifest(
    string ManifestSchemaVersion,
    string ManifestSha256,
    string DataSha256,
    string ConfigurationSha256,
    string StrategyId,
    int StrategyVersion,
    BacktestRunPurpose Purpose,
    IReadOnlyList<BacktestDatasetPartition> Partitions,
    int RandomSeed,
    HistoricalCandleDatasetDescriptor SignalDataset,
    HistoricalCandleDatasetSummary SignalSummary,
    HistoricalCandleDatasetDescriptor TrendDataset,
    HistoricalCandleDatasetSummary TrendSummary,
    ChronologicalDatasetSplit Split);

public static class BacktestRunManifestFactory
{
    public const string SchemaVersion = "backtest-run-manifest-v1";

    public static BacktestRunManifest Create(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution,
        HistoricalCandleDatasetDescriptor signalDataset,
        HistoricalCandleDatasetSummary? signalSummary,
        HistoricalCandleDatasetDescriptor trendDataset,
        HistoricalCandleDatasetSummary? trendSummary,
        ChronologicalDatasetSplit split,
        BacktestExperimentPlan plan,
        int randomSeed)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(plan);
        HistoricalCandleDatasetContract.ValidateDescriptor(signalDataset);
        HistoricalCandleDatasetContract.ValidateDescriptor(trendDataset);
        var completedSignal = signalSummary ?? throw new DomainRuleViolationException(
            "Signal dataset must be read completely before manifest creation.");
        var completedTrend = trendSummary ?? throw new DomainRuleViolationException(
            "Trend dataset must be read completely before manifest creation.");
        ValidateDataset(definition, signalDataset, completedSignal, isSignal: true);
        ValidateDataset(definition, trendDataset, completedTrend, isSignal: false);
        ValidateSplitCoverage(definition, completedSignal, completedTrend, split);
        execution.Validate(definition.SignalTimeframe, definition.InstrumentId);

        var dataHash = Hash(new
        {
            SignalSource = signalDataset.SourceId,
            signalDataset.SchemaVersion,
            signalDataset.Sha256,
            SignalInstrument = signalDataset.InstrumentId.ToString(),
            SignalTimeframeTicks = signalDataset.Timeframe.Duration.Ticks,
            SignalCount = completedSignal.CandleCount,
            SignalFirst = completedSignal.FirstOpenTime,
            SignalLast = completedSignal.LastCloseTime,
            TrendSource = trendDataset.SourceId,
            TrendSchema = trendDataset.SchemaVersion,
            TrendHash = trendDataset.Sha256,
            TrendInstrument = trendDataset.InstrumentId.ToString(),
            TrendTimeframeTicks = trendDataset.Timeframe.Duration.Ticks,
            TrendCount = completedTrend.CandleCount,
            TrendFirst = completedTrend.FirstOpenTime,
            TrendLast = completedTrend.LastCloseTime
        });
        var configurationHash = execution.InstrumentRules is not null
            ? definition.Version >= 3
                ? HashProfitProtectionInstrumentConfiguration(definition, execution)
                : HashInstrumentQuantizedConfiguration(definition, execution)
            : definition.Version >= 3
                ? HashProfitProtectionConfiguration(definition, execution)
                : definition.SignalEmaHysteresisBasisPoints == 0m
                ? HashLegacyConfiguration(definition, execution)
                : HashHysteresisConfiguration(definition, execution);
        var manifestHash = Hash(new
        {
            SchemaVersion,
            DataHash = dataHash,
            ConfigurationHash = configurationHash,
            plan.Purpose,
            Partitions = plan.Partitions.Select(static partition => (int)partition).ToArray(),
            split.StartInclusive,
            split.TrainEndExclusive,
            split.ValidationEndExclusive,
            split.OutOfSampleEndExclusive,
            RandomSeed = randomSeed
        });

        return new BacktestRunManifest(
            SchemaVersion,
            manifestHash,
            dataHash,
            configurationHash,
            definition.StrategyId,
            definition.Version,
            plan.Purpose,
            Array.AsReadOnly(plan.Partitions.ToArray()),
            randomSeed,
            signalDataset,
            completedSignal,
            trendDataset,
            completedTrend,
            split);
    }

    private static string HashLegacyConfiguration(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution) => Hash(new
        {
            definition.StrategyId,
            definition.Version,
            Instrument = definition.InstrumentId.ToString(),
            SignalTimeframeTicks = definition.SignalTimeframe.Duration.Ticks,
            TrendTimeframeTicks = definition.TrendTimeframe.Duration.Ticks,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            execution.InitialQuoteBalance,
            BaseAsset = execution.BaseAsset.Value,
            QuoteAsset = execution.QuoteAsset.Value,
            Allocation = execution.QuoteAllocation.Fraction,
            execution.SyntheticSpreadBasisPoints,
            LatencyTicks = execution.PaperExecution.MinimumLatency.Ticks,
            Commission = execution.PaperExecution.CommissionRate.Fraction,
            execution.PaperExecution.SlippageBasisPoints,
            Participation = execution.PaperExecution.MaximumLiquidityParticipation.Fraction
        });

    private static string HashHysteresisConfiguration(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution) => Hash(new
        {
            StrategyConfigurationSchema = "cost-aware-hysteresis-v1",
            definition.StrategyId,
            definition.Version,
            Instrument = definition.InstrumentId.ToString(),
            SignalTimeframeTicks = definition.SignalTimeframe.Duration.Ticks,
            TrendTimeframeTicks = definition.TrendTimeframe.Duration.Ticks,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            definition.SignalEmaHysteresisBasisPoints,
            execution.InitialQuoteBalance,
            BaseAsset = execution.BaseAsset.Value,
            QuoteAsset = execution.QuoteAsset.Value,
            Allocation = execution.QuoteAllocation.Fraction,
            execution.SyntheticSpreadBasisPoints,
            LatencyTicks = execution.PaperExecution.MinimumLatency.Ticks,
            Commission = execution.PaperExecution.CommissionRate.Fraction,
            execution.PaperExecution.SlippageBasisPoints,
            Participation = execution.PaperExecution.MaximumLiquidityParticipation.Fraction
        });

    private static string HashProfitProtectionConfiguration(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution) => Hash(new
        {
            StrategyConfigurationSchema = "profit-protection-v1",
            definition.StrategyId,
            definition.Version,
            Instrument = definition.InstrumentId.ToString(),
            SignalTimeframeTicks = definition.SignalTimeframe.Duration.Ticks,
            TrendTimeframeTicks = definition.TrendTimeframe.Duration.Ticks,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            definition.SignalEmaHysteresisBasisPoints,
            definition.ReentryCooldownCandles,
            definition.ProfitProtectionActivationBasisPoints,
            definition.ProfitProtectionTrailingBasisPoints,
            execution.InitialQuoteBalance,
            BaseAsset = execution.BaseAsset.Value,
            QuoteAsset = execution.QuoteAsset.Value,
            Allocation = execution.QuoteAllocation.Fraction,
            execution.SyntheticSpreadBasisPoints,
            LatencyTicks = execution.PaperExecution.MinimumLatency.Ticks,
            Commission = execution.PaperExecution.CommissionRate.Fraction,
            execution.PaperExecution.SlippageBasisPoints,
            Participation = execution.PaperExecution.MaximumLiquidityParticipation.Fraction
        });

    private static string HashInstrumentQuantizedConfiguration(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution)
    {
        var instrument = execution.InstrumentRules!;
        return Hash(new
        {
            ConfigurationSchema = "instrument-quantized-backtest-v1",
            StrategyConfigurationSchema = definition.SignalEmaHysteresisBasisPoints == 0m
                ? "legacy-v1"
                : "cost-aware-hysteresis-v1",
            definition.StrategyId,
            definition.Version,
            Instrument = definition.InstrumentId.ToString(),
            SignalTimeframeTicks = definition.SignalTimeframe.Duration.Ticks,
            TrendTimeframeTicks = definition.TrendTimeframe.Duration.Ticks,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            definition.SignalEmaHysteresisBasisPoints,
            execution.InitialQuoteBalance,
            BaseAsset = execution.BaseAsset.Value,
            QuoteAsset = execution.QuoteAsset.Value,
            Allocation = execution.QuoteAllocation.Fraction,
            execution.SyntheticSpreadBasisPoints,
            LatencyTicks = execution.PaperExecution.MinimumLatency.Ticks,
            Commission = execution.PaperExecution.CommissionRate.Fraction,
            execution.PaperExecution.SlippageBasisPoints,
            Participation = execution.PaperExecution.MaximumLiquidityParticipation.Fraction,
            RulesInstrument = instrument.Id.ToString(),
            instrument.PriceTickSize,
            instrument.QuantityStepSize,
            instrument.MinimumQuantity,
            instrument.MinimumNotional
        });
    }

    private static string HashProfitProtectionInstrumentConfiguration(
        StrategyDefinition definition,
        BacktestExecutionPolicy execution)
    {
        var instrument = execution.InstrumentRules!;
        return Hash(new
        {
            ConfigurationSchema = "instrument-quantized-backtest-v1",
            StrategyConfigurationSchema = "profit-protection-v1",
            definition.StrategyId,
            definition.Version,
            Instrument = definition.InstrumentId.ToString(),
            SignalTimeframeTicks = definition.SignalTimeframe.Duration.Ticks,
            TrendTimeframeTicks = definition.TrendTimeframe.Duration.Ticks,
            definition.SignalEmaPeriod,
            definition.TrendEmaPeriod,
            definition.MaximumSignalCandleMovePercent,
            definition.MinimumSignalWarmupCandles,
            definition.MinimumTrendWarmupCandles,
            definition.SignalEmaHysteresisBasisPoints,
            definition.ReentryCooldownCandles,
            definition.ProfitProtectionActivationBasisPoints,
            definition.ProfitProtectionTrailingBasisPoints,
            execution.InitialQuoteBalance,
            BaseAsset = execution.BaseAsset.Value,
            QuoteAsset = execution.QuoteAsset.Value,
            Allocation = execution.QuoteAllocation.Fraction,
            execution.SyntheticSpreadBasisPoints,
            LatencyTicks = execution.PaperExecution.MinimumLatency.Ticks,
            Commission = execution.PaperExecution.CommissionRate.Fraction,
            execution.PaperExecution.SlippageBasisPoints,
            Participation = execution.PaperExecution.MaximumLiquidityParticipation.Fraction,
            RulesInstrument = instrument.Id.ToString(),
            instrument.PriceTickSize,
            instrument.QuantityStepSize,
            instrument.MinimumQuantity,
            instrument.MinimumNotional
        });
    }

    private static void ValidateDataset(
        StrategyDefinition definition,
        HistoricalCandleDatasetDescriptor descriptor,
        HistoricalCandleDatasetSummary summary,
        bool isSignal)
    {
        var expectedTimeframe = isSignal ? definition.SignalTimeframe : definition.TrendTimeframe;
        if (descriptor.InstrumentId != definition.InstrumentId ||
            descriptor.Timeframe != expectedTimeframe ||
            summary.CandleCount <= 0 || summary.FirstOpenTime >= summary.LastCloseTime)
        {
            throw new DomainRuleViolationException(
                "Historical dataset does not match the strategy or has no completed candles.");
        }
    }

    private static string Hash<T>(T value)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static void ValidateSplitCoverage(
        StrategyDefinition definition,
        HistoricalCandleDatasetSummary signal,
        HistoricalCandleDatasetSummary trend,
        ChronologicalDatasetSplit split)
    {
        DateTimeOffset[] boundaries =
        [
            split.StartInclusive,
            split.TrainEndExclusive,
            split.ValidationEndExclusive,
            split.OutOfSampleEndExclusive
        ];
        if (boundaries.Any(boundary =>
                !definition.SignalTimeframe.IsBoundary(boundary) ||
                !definition.TrendTimeframe.IsBoundary(boundary)) ||
            signal.FirstOpenTime > split.StartInclusive ||
            trend.FirstOpenTime > split.StartInclusive ||
            signal.LastCloseTime < split.OutOfSampleEndExclusive ||
            trend.LastCloseTime < split.OutOfSampleEndExclusive)
        {
            throw new DomainRuleViolationException(
                "Backtest split must align to both timeframes and be covered by both datasets.");
        }
    }
}
