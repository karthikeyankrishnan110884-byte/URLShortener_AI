using System.Collections.Concurrent;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// Thread-safe in-memory store ("simple mode"). Codes are case-sensitive.
/// Data is lost on restart; acceptable for the prototype, see docs/RISKS.md.
/// </summary>
public sealed class InMemoryLinkRepository : ILinkRepository
{
    private readonly ConcurrentDictionary<string, ShortLink> _links = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ClickEvent>> _clicks = new(StringComparer.Ordinal);

    public bool TryAdd(ShortLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return _links.TryAdd(link.Code, link);
    }

    public ShortLink? GetByCode(string code) =>
        _links.TryGetValue(code, out var link) ? link : null;

    public void AddClick(ClickEvent click)
    {
        ArgumentNullException.ThrowIfNull(click);
        _clicks.GetOrAdd(click.Code, _ => new ConcurrentQueue<ClickEvent>()).Enqueue(click);
    }

    public IReadOnlyList<ClickEvent> GetClicks(string code) =>
        _clicks.TryGetValue(code, out var queue) ? queue.ToArray() : [];
}
