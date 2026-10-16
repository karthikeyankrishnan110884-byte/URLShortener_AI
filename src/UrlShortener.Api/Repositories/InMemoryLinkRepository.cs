using System.Collections.Concurrent;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// Thread-safe in-memory link store ("simple mode"). Codes are case-sensitive.
/// Data is lost on restart; acceptable for the prototype, see docs/RISKS.md.
/// </summary>
public sealed class InMemoryLinkRepository : ILinkRepository
{
    private readonly ConcurrentDictionary<string, ShortLink> _links = new(StringComparer.Ordinal);

    public ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        return ValueTask.FromResult(_links.TryAdd(link.Code, link));
    }

    public ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_links.TryGetValue(code, out var link) ? link : null);

    public ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        _links[link.Code] = link;
        return ValueTask.CompletedTask;
    }
}
