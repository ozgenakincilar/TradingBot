using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.MarketData;

public enum ClosedCandleIntegrityStatus
{
    AwaitingRecovery = 1,
    RecoveryApplied = 2,
    RecoveryRejected = 3,
    Accepted = 4,
    Duplicate = 5,
    OutOfOrder = 6,
    GapDetected = 7,
    ConflictingCandle = 8
}

public sealed record ClosedCandleIntegrityResult(
    ClosedCandleIntegrityStatus Status,
    bool IsReady,
    DateTimeOffset? LastAcceptedOpenTime,
    DateTimeOffset? ExpectedOpenTime);

public sealed class ClosedCandleSequenceGuard
{
    private readonly InstrumentId _instrumentId;
    private readonly Timeframe _timeframe;
    private Candle? _lastAccepted;

    public ClosedCandleSequenceGuard(InstrumentId instrumentId, Timeframe timeframe)
    {
        if (instrumentId == default || timeframe == default)
        {
            throw new ArgumentException("Instrument and timeframe are required.");
        }

        _instrumentId = instrumentId;
        _timeframe = timeframe;
    }

    public bool IsReady { get; private set; }

    public ClosedCandleIntegrityResult Observe(Candle candle)
    {
        ValidateIdentity(candle);
        if (!IsReady || _lastAccepted is null)
        {
            return Result(ClosedCandleIntegrityStatus.AwaitingRecovery);
        }

        if (candle.OpenTime == _lastAccepted.OpenTime)
        {
            if (candle == _lastAccepted)
            {
                return Result(ClosedCandleIntegrityStatus.Duplicate);
            }

            IsReady = false;
            return Result(ClosedCandleIntegrityStatus.ConflictingCandle);
        }

        if (candle.OpenTime < _lastAccepted.OpenTime)
        {
            return Result(ClosedCandleIntegrityStatus.OutOfOrder);
        }

        if (candle.OpenTime != _lastAccepted.CloseTime)
        {
            IsReady = false;
            return Result(ClosedCandleIntegrityStatus.GapDetected);
        }

        _lastAccepted = candle;
        return Result(ClosedCandleIntegrityStatus.Accepted);
    }

    public ClosedCandleIntegrityResult ApplyRecovery(IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        IsReady = false;
        if (candles.Count == 0)
        {
            return Result(ClosedCandleIntegrityStatus.RecoveryRejected);
        }

        var expectedOpenTime = _lastAccepted?.CloseTime;
        Candle? candidate = null;
        foreach (var candle in candles)
        {
            ValidateIdentity(candle);
            if (expectedOpenTime is not null && candle.OpenTime != expectedOpenTime)
            {
                return Result(ClosedCandleIntegrityStatus.RecoveryRejected);
            }

            candidate = candle;
            expectedOpenTime = candle.CloseTime;
        }

        _lastAccepted = candidate;
        IsReady = true;
        return Result(ClosedCandleIntegrityStatus.RecoveryApplied);
    }

    private void ValidateIdentity(Candle candle)
    {
        ArgumentNullException.ThrowIfNull(candle);
        if (candle.InstrumentId != _instrumentId || candle.Timeframe != _timeframe)
        {
            throw new DomainRuleViolationException("Candle belongs to another instrument or timeframe.");
        }
    }

    private ClosedCandleIntegrityResult Result(ClosedCandleIntegrityStatus status) =>
        new(
            status,
            IsReady,
            _lastAccepted?.OpenTime,
            _lastAccepted?.CloseTime);
}
