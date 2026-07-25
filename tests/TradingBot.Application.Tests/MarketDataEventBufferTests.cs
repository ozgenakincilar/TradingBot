using TradingBot.Application.Abstractions;
using TradingBot.Application.MarketData;
using TradingBot.Domain.Execution;
using TradingBot.Domain.Instruments;

namespace TradingBot.Application.Tests;

public sealed class MarketDataEventBufferTests
{
    private static readonly InstrumentId Instrument = InstrumentId.Create("PAPER", "BTCUSDT");
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FullBufferAppliesBackpressureUntilReaderConsumes()
    {
        var buffer = new MarketDataEventBuffer(capacity: 1);
        await buffer.WriteAsync(Event(1), CancellationToken.None);

        var blockedWrite = buffer.WriteAsync(Event(2), CancellationToken.None).AsTask();
        Assert.False(blockedWrite.IsCompleted);

        await using var reader = buffer.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(1, reader.Current.Sequence);
        await blockedWrite;
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(2, reader.Current.Sequence);
        buffer.Complete();
    }

    [Fact]
    public async Task PendingWriteHonorsCancellation()
    {
        var buffer = new MarketDataEventBuffer(capacity: 1);
        await buffer.WriteAsync(Event(1), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var write = async () => await buffer.WriteAsync(Event(2), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(write);
        buffer.Complete();
    }

    private static PaperMarketEvent Event(long sequence) =>
        new(
            $"event-{sequence}",
            sequence,
            Now.AddMilliseconds(sequence),
            new PaperTopOfBookSnapshot(
                Instrument,
                Price.From(99m),
                1m,
                Price.From(100m),
                1m,
                Now.AddMilliseconds(sequence)));
}
