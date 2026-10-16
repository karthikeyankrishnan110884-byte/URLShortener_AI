using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// Cache-aside decorator for the redirect hot path, registered via DI so services are unchanged (ADR-005).
/// <list type="bullet">
/// <item>Hit: served from memory. Miss: one load per key at a time (request coalescing, prevents stampedes on hot keys).</item>
/// <item>Entries live for <see cref="CacheOptions.LinkTtlSeconds"/>, never past the link's own expiry.</item>
/// <item>Unknown codes are negatively cached briefly; a successful add clears the negative entry.</item>
/// <item><see cref="UpdateAsync"/> (deactivate) evicts the entry on this node; other nodes converge within the TTL.</item>
/// </list>
/// </summary>
public sealed class CachedLinkRepository(
    ILinkRepository inner,
    IMemoryCache cache,
    IOptions<CacheOptions> options,
    TimeProvider timeProvider) : ILinkRepository
{
    private static readonly object NotFound = new();

    private readonly CacheOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, Lazy<Task<ShortLink?>>> _inflight = new(StringComparer.Ordinal);

    public async ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        var added = await inner.TryAddAsync(link, cancellationToken);
        if (added)
        {
            cache.Remove(Key(link.Code));
        }

        return added;
    }

    public async ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(Key(code), out var cached))
        {
            return cached as ShortLink;
        }

        var load = _inflight.GetOrAdd(code, c => new Lazy<Task<ShortLink?>>(() => LoadAsync(c)));
        try
        {
            return await load.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<ShortLink?>>>(code, load));
        }
    }

    public async ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        await inner.UpdateAsync(link, cancellationToken);
        cache.Remove(Key(link.Code));
    }

    private async Task<ShortLink?> LoadAsync(string code)
    {
        // Not tied to one caller's token: other callers may be waiting on the same load.
        var link = await inner.GetByCodeAsync(code, CancellationToken.None);
        var entry = new MemoryCacheEntryOptions { Size = 1 };

        if (link is null)
        {
            if (_options.NotFoundTtlSeconds > 0)
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.NotFoundTtlSeconds);
                cache.Set(Key(code), NotFound, entry);
            }

            return null;
        }

        var expiresAt = timeProvider.GetUtcNow().AddSeconds(_options.LinkTtlSeconds);
        if (link.ExpiresAt is { } linkExpiry && linkExpiry < expiresAt)
        {
            expiresAt = linkExpiry;
        }

        entry.AbsoluteExpiration = expiresAt;
        cache.Set(Key(code), link, entry);
        return link;
    }

    private static string Key(string code) => "link:" + code;
}
