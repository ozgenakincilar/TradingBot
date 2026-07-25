using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TradingBot.Application.Abstractions;

namespace TradingBot.Application.MarketData;

public sealed class MarketDataEventBuffer
{
    private readonly Channel<PaperMarketEvent> _channel;

    public MarketDataEventBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _channel = Channel.CreateBounded<PaperMarketEvent>(new BoundedChannelOptions(capacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask WriteAsync(
        PaperMarketEvent marketEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(marketEvent);
        return _channel.Writer.WriteAsync(marketEvent, cancellationToken);
    }

    public async IAsyncEnumerable<PaperMarketEvent> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var marketEvent in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return marketEvent;
        }
    }

    public void Complete(Exception? error = null) =>
        _channel.Writer.TryComplete(error);
}
