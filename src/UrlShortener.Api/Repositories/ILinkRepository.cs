using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// Link storage abstraction. Async so a SQL/Redis implementation can be swapped in without changing callers.
/// </summary>
public interface ILinkRepository
{
    /// <summary>Atomically adds the link. Returns false if the code already exists (uniqueness guarantee; SQL: unique index).</summary>
    ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default);

    ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Persists a state change (e.g. deactivation). Decorators use this to invalidate caches.</summary>
    ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default);
}
