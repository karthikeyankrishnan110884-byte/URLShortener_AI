using UrlShortener.Api.Domain;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>Applies clicks to storage: the link's counters and the analytics aggregates.</summary>
public interface IClickIngestor
{
    ValueTask IngestAsync(ClickEvent click, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch path used by the processor: one counter update per distinct link and one bulk insert per batch,
    /// so throughput is bounded by round-trips per batch, not per click (found by the load test, see B-brownfield.md).
    /// </summary>
    ValueTask IngestBatchAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken = default);
}

public sealed class ClickIngestor(ILinkRepository links, IClickRepository clicks) : IClickIngestor
{
    public async ValueTask IngestAsync(ClickEvent click, CancellationToken cancellationToken = default)
    {
        var link = await links.GetByCodeAsync(click.Code, cancellationToken);
        link?.RegisterClick(click.OccurredAt);
        await clicks.AddAsync(click, cancellationToken);
    }

    public async ValueTask IngestBatchAsync(IReadOnlyList<ClickEvent> batch, CancellationToken cancellationToken = default)
    {
        if (batch.Count == 0)
        {
            return;
        }

        // SQL equivalent: UPDATE links SET click_count = click_count + @n, last_accessed_at = GREATEST(...) per code.
        foreach (var group in batch.GroupBy(c => c.Code, StringComparer.Ordinal))
        {
            var link = await links.GetByCodeAsync(group.Key, cancellationToken);
            link?.RegisterClicks(group.LongCount(), group.Max(c => c.OccurredAt));
        }

        await clicks.AddRangeAsync(batch, cancellationToken);
    }
}
