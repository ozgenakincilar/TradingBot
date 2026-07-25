using TradingBot.Domain.Common;
using TradingBot.Domain.Instruments;

namespace TradingBot.Domain.MarketData;

public sealed record MarketDataCursor(
    InstrumentId InstrumentId,
    string EventId,
    long Sequence,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt)
{
    public void Validate()
    {
        if (InstrumentId == default)
        {
            throw new DomainRuleViolationException("Market data instrument is required.");
        }

        if (string.IsNullOrWhiteSpace(EventId) || EventId.Length > 128)
        {
            throw new DomainRuleViolationException("Market data event id is invalid.");
        }

        if (Sequence <= 0 || OccurredAt == default || ReceivedAt == default)
        {
            throw new DomainRuleViolationException("Market data sequence and timestamps are required.");
        }
    }
}

public enum MarketDataIntegrityStatus
{
    AwaitingRecovery = 1,
    RecoveryApplied = 2,
    RecoveryRejected = 3,
    Accepted = 4,
    Duplicate = 5,
    OutOfOrder = 6,
    GapDetected = 7,
    ConflictingSequence = 8,
    TimestampRegression = 9
}

public sealed record MarketDataIntegrityResult(
    MarketDataIntegrityStatus Status,
    bool IsReady,
    long? LastAcceptedSequence,
    long? ExpectedSequence);

public sealed class MarketDataIntegrityGuard
{
    private readonly InstrumentId _instrumentId;
    private string? _lastEventId;
    private long? _lastSequence;
    private DateTimeOffset? _lastOccurredAt;
    private DateTimeOffset? _lastReceivedAt;

    public MarketDataIntegrityGuard(InstrumentId instrumentId)
    {
        if (instrumentId == default)
        {
            throw new ArgumentException("Instrument is required.", nameof(instrumentId));
        }

        _instrumentId = instrumentId;
    }

    public bool IsReady { get; private set; }

    public MarketDataIntegrityResult Observe(MarketDataCursor marketEvent)
    {
        ValidateCursor(marketEvent);
        if (!IsReady || _lastSequence is null)
        {
            return Result(MarketDataIntegrityStatus.AwaitingRecovery);
        }

        if (marketEvent.Sequence == _lastSequence)
        {
            if (string.Equals(marketEvent.EventId, _lastEventId, StringComparison.Ordinal))
            {
                return Result(MarketDataIntegrityStatus.Duplicate);
            }

            IsReady = false;
            return Result(MarketDataIntegrityStatus.ConflictingSequence);
        }

        if (marketEvent.Sequence < _lastSequence)
        {
            return Result(MarketDataIntegrityStatus.OutOfOrder);
        }

        if (marketEvent.Sequence != _lastSequence + 1)
        {
            IsReady = false;
            return Result(MarketDataIntegrityStatus.GapDetected);
        }

        if (marketEvent.OccurredAt < _lastOccurredAt || marketEvent.ReceivedAt < _lastReceivedAt)
        {
            IsReady = false;
            return Result(MarketDataIntegrityStatus.TimestampRegression);
        }

        Accept(marketEvent);
        return Result(MarketDataIntegrityStatus.Accepted);
    }

    public MarketDataIntegrityResult ApplyRecoverySnapshot(MarketDataCursor snapshot)
    {
        ValidateCursor(snapshot);
        if ((_lastSequence is not null && snapshot.Sequence < _lastSequence) ||
            (_lastOccurredAt is not null && snapshot.OccurredAt < _lastOccurredAt) ||
            (_lastReceivedAt is not null && snapshot.ReceivedAt < _lastReceivedAt))
        {
            return Result(MarketDataIntegrityStatus.RecoveryRejected);
        }

        Accept(snapshot);
        IsReady = true;
        return Result(MarketDataIntegrityStatus.RecoveryApplied);
    }

    public bool IsFresh(DateTimeOffset now, TimeSpan maximumAge)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        return IsReady &&
               _lastReceivedAt is not null &&
               now >= _lastReceivedAt &&
               now - _lastReceivedAt <= maximumAge;
    }

    private void ValidateCursor(MarketDataCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        cursor.Validate();
        if (cursor.InstrumentId != _instrumentId)
        {
            throw new DomainRuleViolationException("Market data cursor belongs to another instrument.");
        }
    }

    private void Accept(MarketDataCursor cursor)
    {
        _lastEventId = cursor.EventId;
        _lastSequence = cursor.Sequence;
        _lastOccurredAt = cursor.OccurredAt;
        _lastReceivedAt = cursor.ReceivedAt;
    }

    private MarketDataIntegrityResult Result(MarketDataIntegrityStatus status) =>
        new(status, IsReady, _lastSequence, _lastSequence + 1);
}
