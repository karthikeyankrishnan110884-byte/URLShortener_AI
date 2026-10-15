using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Services;

public interface IUrlShortenerService
{
    ShortLink Create(CreateLinkRequest request);

    ShortLink Get(string code);

    /// <summary>Resolves a code for redirect and records the click. Throws when missing, expired or deactivated.</summary>
    Uri ResolveAndTrack(string code, string? referrer, string? userAgent);

    LinkStatsResponse GetStats(string code, int topReferrers = 5);

    void Deactivate(string code);
}
