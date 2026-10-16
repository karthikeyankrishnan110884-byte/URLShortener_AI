using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Api.Services;

/// <summary>Result of a create: <see cref="Replayed"/> is true when an Idempotency-Key matched an earlier request.</summary>
public sealed record CreateLinkResult(ShortLink Link, bool Replayed);

/// <summary>Link lifecycle: create, read, resolve for redirect, deactivate.</summary>
public interface IUrlShortenerService
{
    Task<CreateLinkResult> CreateAsync(CreateLinkRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    Task<ShortLink> GetAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Resolves a code for redirect and records the click. Throws when missing, expired, deactivated or blocked.</summary>
    Task<Uri> ResolveAsync(string code, ClickContext context, CancellationToken cancellationToken = default);

    /// <summary>Resolves without recording (HEAD requests, link previews).</summary>
    Task<Uri> PeekAsync(string code, CancellationToken cancellationToken = default);

    Task DeactivateAsync(string code, CancellationToken cancellationToken = default);
}
