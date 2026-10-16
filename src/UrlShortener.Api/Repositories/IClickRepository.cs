using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>Click analytics storage, separate from link storage so each can scale (and be swapped) independently.</summary>
public interface IClickRepository
{
    ValueTask AddAsync(ClickEvent click, CancellationToken cancellationToken = default);

    /// <summary>Bulk insert (SQL: one multi-row INSERT / SqlBulkCopy). Used by the background processor.</summary>
    ValueTask AddRangeAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken = default);

    ValueTask<ClickSummary> GetSummaryAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Most recent raw events (bounded by configuration), oldest first.</summary>
    ValueTask<IReadOnlyList<ClickEvent>> GetRecentAsync(string code, CancellationToken cancellationToken = default);
}
