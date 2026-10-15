namespace UrlShortener.Api.Domain;

/// <summary>A single redirect. No IP address is stored, by design (privacy).</summary>
public sealed record ClickEvent(string Code, DateTimeOffset OccurredAt, string? ReferrerHost, string? UserAgent);
