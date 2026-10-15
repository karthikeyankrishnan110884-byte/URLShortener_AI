using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>Storage abstraction. Swap the in-memory implementation for SQL/Redis without touching services.</summary>
public interface ILinkRepository
{
    /// <summary>Atomically adds the link. Returns false if the code already exists (uniqueness guarantee).</summary>
    bool TryAdd(ShortLink link);

    ShortLink? GetByCode(string code);

    void AddClick(ClickEvent click);

    IReadOnlyList<ClickEvent> GetClicks(string code);
}
