namespace UrlShortener.Api.Contracts;

/// <summary>
/// Public view of a short link. <c>ClickCount</c> and <c>LastAccessedAt</c> are eventually consistent:
/// clicks are recorded asynchronously (typically within milliseconds).
/// </summary>
public sealed record LinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    long ClickCount,
    bool IsCustomAlias = false,
    DateTimeOffset? DeactivatedAt = null,
    DateTimeOffset? LastAccessedAt = null);
