namespace UrlShortener.Api.Domain;

/// <summary>A single redirect. No IP address is stored, by design (privacy, see ADR-007).</summary>
public sealed record ClickEvent(
    string Code,
    DateTimeOffset OccurredAt,
    string? ReferrerHost,
    string? UserAgent,
    UtmTags? Utm = null,
    string? Device = null);

/// <summary>Campaign tags taken from the short URL's query string (e.g. <c>/abc?utm_source=newsletter</c>).</summary>
public sealed record UtmTags(string? Source, string? Medium, string? Campaign)
{
    public static readonly UtmTags None = new(null, null, null);

    public bool IsEmpty => Source is null && Medium is null && Campaign is null;
}
