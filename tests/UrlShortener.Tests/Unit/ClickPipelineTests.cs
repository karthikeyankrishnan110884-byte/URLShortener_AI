using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Tests.Unit;

public class ClickPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static ChannelClickQueue Queue(int capacity = 1000) =>
        new(Options.Create(new AnalyticsOptions { QueueCapacity = capacity }));

    [Fact]
    public async Task Queue_WhenFull_DropsAndCounts_WithoutBlockingTheCaller()
    {
        var queue = Queue(capacity: 100);

        for (var i = 0; i < 150; i++)
        {
            var record = queue.RecordAsync(new ClickEvent("a", Now, null, null));
            Assert.True(record.IsCompletedSuccessfully); // never waits on the redirect path
            await record;
        }

        Assert.Equal(100, queue.Enqueued);
        Assert.Equal(50, queue.Dropped);
        Assert.Equal(100, queue.Pending);
    }

    [Fact]
    public async Task Processor_DrainsEveryEvent_IntoStorage()
    {
        var (queue, processor, links, clicks) = await StartPipelineAsync();
        var link = new ShortLink("abc1234", new Uri("https://example.com"), Now, null, false);
        await links.TryAddAsync(link);

        for (var i = 0; i < 500; i++)
        {
            await queue.RecordAsync(new ClickEvent("abc1234", Now.AddSeconds(i), "ref.example", null));
        }

        await WaitUntilAsync(() => processor.Processed == 500);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(500, link.ClickCount);
        Assert.Equal(Now.AddSeconds(499), link.LastAccessedAt);
        Assert.Equal(500, (await clicks.GetSummaryAsync("abc1234")).Total);
        Assert.Equal(0, processor.Failed);
        Assert.True(processor.Batches < 500, "events should be written in batches, not one round-trip each");
    }

    [Fact]
    public async Task Processor_OnGracefulStop_DrainsWhatIsStillQueued()
    {
        var queue = Queue();
        var clicks = new InMemoryClickRepository();
        var processor = new ClickEventProcessor(queue, new ClickIngestor(new InMemoryLinkRepository(), clicks), BatchOptions(), NullLogger<ClickEventProcessor>.Instance);

        // Enqueue before the processor starts, then start+stop immediately: the drain loop must pick them up.
        for (var i = 0; i < 100; i++)
        {
            await queue.RecordAsync(new ClickEvent("x", Now, null, null));
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await processor.StartAsync(cts.Token);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(100, (await clicks.GetSummaryAsync("x")).Total);
    }

    [Fact]
    public async Task Processor_SurvivesAFailingWrite_AndKeepsGoing()
    {
        var queue = Queue();
        var clicks = new InMemoryClickRepository();
        var processor = new ClickEventProcessor(queue, new FlakyIngestor(new ClickIngestor(new InMemoryLinkRepository(), clicks)), BatchOptions(maxBatch: 1), NullLogger<ClickEventProcessor>.Instance);
        await processor.StartAsync(CancellationToken.None);

        await queue.RecordAsync(new ClickEvent("boom", Now, null, null));
        await queue.RecordAsync(new ClickEvent("ok", Now, null, null));

        await WaitUntilAsync(() => processor.Processed + processor.Failed == 2);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal(1, processor.Failed);
        Assert.Equal(1, (await clicks.GetSummaryAsync("ok")).Total);
    }

    [Fact]
    public async Task HealthCheck_IsDegraded_WhenQueueIsNearlyFull()
    {
        var queue = Queue(capacity: 100);
        var check = new ClickQueueHealthCheck(queue);

        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);

        for (var i = 0; i < 95; i++)
        {
            await queue.RecordAsync(new ClickEvent("a", Now, null, null));
        }

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(95, result.Data["pending"]);
    }

    [Fact]
    public async Task IngestBatch_UpdatesEachLinkOnce_AndInsertsEveryEvent()
    {
        var links = new TestDoubles.CountingLinkRepository(new InMemoryLinkRepository());
        var a = new ShortLink("aaa", new Uri("https://a.example"), Now, null, false);
        var b = new ShortLink("bbb", new Uri("https://b.example"), Now, null, false);
        await links.TryAddAsync(a);
        await links.TryAddAsync(b);
        var clicks = new InMemoryClickRepository();
        var batch = Enumerable.Range(0, 30).Select(i => new ClickEvent(i % 3 == 0 ? "bbb" : "aaa", Now.AddSeconds(i), null, null)).ToList();

        await new ClickIngestor(links, clicks).IngestBatchAsync(batch);
        await new ClickIngestor(links, clicks).IngestBatchAsync([]);

        Assert.Equal(2, links.Reads);
        Assert.Equal(20, a.ClickCount);
        Assert.Equal(10, b.ClickCount);
        Assert.Equal(30, (await clicks.GetSummaryAsync("aaa")).Total + (await clicks.GetSummaryAsync("bbb")).Total);
    }

    [Fact]
    public async Task InlineRecorder_WritesImmediately()
    {
        var links = new InMemoryLinkRepository();
        var clicks = new InMemoryClickRepository();
        var recorder = new InlineClickRecorder(new ClickIngestor(links, clicks));

        await recorder.RecordAsync(new ClickEvent("a", Now, null, null));

        Assert.Equal(1, (await clicks.GetSummaryAsync("a")).Total);
    }

    private static async Task<(ChannelClickQueue, ClickEventProcessor, InMemoryLinkRepository, InMemoryClickRepository)> StartPipelineAsync()
    {
        var queue = Queue();
        var links = new InMemoryLinkRepository();
        var clicks = new InMemoryClickRepository();
        var processor = new ClickEventProcessor(queue, new ClickIngestor(links, clicks), BatchOptions(), NullLogger<ClickEventProcessor>.Instance);
        await processor.StartAsync(CancellationToken.None);
        return (queue, processor, links, clicks);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met within 10s.");
            await Task.Delay(10);
        }
    }

    private static IOptions<AnalyticsOptions> BatchOptions(int maxBatch = 1000) =>
        Options.Create(new AnalyticsOptions { MaxBatchSize = maxBatch });

    private sealed class FlakyIngestor(IClickIngestor inner) : IClickIngestor
    {
        public ValueTask IngestAsync(ClickEvent click, CancellationToken cancellationToken = default) =>
            inner.IngestAsync(click, cancellationToken);

        public ValueTask IngestBatchAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken = default) =>
            clicks.Any(c => c.Code == "boom") ? throw new InvalidOperationException("storage down") : inner.IngestBatchAsync(clicks, cancellationToken);
    }
}
