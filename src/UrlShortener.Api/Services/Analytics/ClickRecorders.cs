using System.Threading.Channels;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>Called on the redirect path. Implementations decide whether the write happens now or later.</summary>
public interface IClickRecorder
{
    ValueTask RecordAsync(ClickEvent click, CancellationToken cancellationToken = default);
}

/// <summary>Writes on the request thread. Deterministic; used for comparison ("before") and unit tests.</summary>
public sealed class InlineClickRecorder(IClickIngestor ingestor) : IClickRecorder
{
    public ValueTask RecordAsync(ClickEvent click, CancellationToken cancellationToken = default) =>
        ingestor.IngestAsync(click, cancellationToken);
}

/// <summary>
/// Moves analytics off the redirect hot path (ADR-004). A bounded <see cref="Channel{T}"/> decouples redirects
/// from storage; <see cref="ClickEventProcessor"/> drains it. When the queue is full the event is dropped and
/// counted rather than slowing redirects (availability over analytics completeness, by design).
/// </summary>
public sealed class ChannelClickQueue : IClickRecorder
{
    private readonly Channel<ClickEvent> _channel;
    private long _enqueued;
    private long _dropped;

    public ChannelClickQueue(IOptions<AnalyticsOptions> options)
    {
        Capacity = options.Value.QueueCapacity;
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait, // with TryWrite this means "reject when full", never block
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public int Capacity { get; }

    public int Pending => _channel.Reader.Count;

    public long Enqueued => Interlocked.Read(ref _enqueued);

    public long Dropped => Interlocked.Read(ref _dropped);

    internal ChannelReader<ClickEvent> Reader => _channel.Reader;

    public ValueTask RecordAsync(ClickEvent click, CancellationToken cancellationToken = default)
    {
        if (_channel.Writer.TryWrite(click))
        {
            Interlocked.Increment(ref _enqueued);
        }
        else
        {
            Interlocked.Increment(ref _dropped);
        }

        return ValueTask.CompletedTask;
    }
}
