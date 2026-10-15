using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Infrastructure;

public static class LinkMapper
{
    public static LinkResponse ToResponse(this ShortLink link, string baseUrl) => new(
        link.Code,
        $"{baseUrl.TrimEnd('/')}/{link.Code}",
        link.OriginalUrl.AbsoluteUri,
        link.CreatedAt,
        link.ExpiresAt,
        link.IsActive,
        link.ClickCount);
}
