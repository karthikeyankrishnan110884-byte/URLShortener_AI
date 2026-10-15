namespace UrlShortener.Api.Contracts;

/// <summary>Public view of a short link.</summary>
public sealed record LinkResponse(
    string Code,
    string ShortUrl,
    string OriginalUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    long ClickCount);
