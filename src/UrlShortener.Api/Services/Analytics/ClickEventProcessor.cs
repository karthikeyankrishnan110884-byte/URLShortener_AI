using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>
/// Single consumer of <see cref="ChannelClickQueue"/>. Drains whatever is available (up to <c>Analytics:MaxBatchSize</c>)
/// and writes it as one batch. On graceful shutdown it drains the queue. On a crash, queued events
/// (at most <c>Analytics:QueueCapacity</c>) are lost: see docs/SCENARIOS/B-brownfield.md.
/// </summary>
public sealed partial class ClickEventProcessor(
    ChannelClickQueue queue,
    IClickIngestor ingestor,
    IOptions<AnalyticsOptions> options,
    ILogger<ClickEventProcessor> logger) : BackgroundService
{
    private readonly int _maxBatch = options.Value.MaxBatchSize;
    private long _processed;
    private long _failed;
    private long _batches;

    public long Processed => Interlocked.Read(ref _processed);

    public long Failed => Interlocked.Read(ref _failed);

    public long Batches => Interlocked.Read(ref _batches);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>(_maxBatch);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await DrainAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: fall through to the final drain.
        }

        var before = Processed;
        await DrainAsync(batch);
        LogStopped(logger, Processed, Processed - before, Failed, queue.Dropped);
    }

    private async Task DrainAsync(List<ClickEvent> batch)
    {
        while (true)
        {
            batch.Clear();
            while (batch.Count < _maxBatch && queue.Reader.TryRead(out var click))
            {
                batch.Add(click);
            }

            if (batch.Count == 0)
            {
                return;
            }

            await IngestSafelyAsync(batch);
        }
    }

    private async ValueTask IngestSafelyAsync(List<ClickEvent> batch)
    {
        try
        {
            await ingestor.IngestBatchAsync(batch, CancellationToken.None);
            Interlocked.Add(ref _processed, batch.Count);
            Interlocked.Increment(ref _batches);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A failing batch must not stop the pipeline. Production: retry with backoff, then dead-letter.
            Interlocked.Add(ref _failed, batch.Count);
            LogIngestFailed(logger, ex, batch.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record a batch of {Count} clicks")]
    private static partial void LogIngestFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Click processor stopped: processed={Processed} drainedOnStop={Drained} failed={Failed} dropped={Dropped}")]
    private static partial void LogStopped(ILogger logger, long processed, long drained, long failed, long dropped);
}
