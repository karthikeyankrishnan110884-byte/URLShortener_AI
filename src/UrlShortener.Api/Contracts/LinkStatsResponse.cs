namespace UrlShortener.Api.Contracts;

/// <summary>Click analytics for a short link.</summary>
public sealed record LinkStatsResponse(
    string Code,
    long TotalClicks,
    DateTimeOffset? LastAccessedAt,
    IReadOnlyList<DailyClicks> ClicksByDay,
    IReadOnlyList<ReferrerClicks> TopReferrers);

public sealed record DailyClicks(DateOnly Date, int Clicks);

public sealed record ReferrerClicks(string Referrer, int Clicks);
