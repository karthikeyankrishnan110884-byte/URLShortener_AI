namespace UrlShortener.Api.Contracts;

/// <summary>Click analytics for a short link. Totals are exact; "top" lists show the N largest values.</summary>
public sealed record LinkStatsResponse(
    string Code,
    long TotalClicks,
    DateTimeOffset? LastAccessedAt,
    IReadOnlyList<DailyClicks> ClicksByDay,
    IReadOnlyList<ReferrerClicks> TopReferrers,
    IReadOnlyList<ValueClicks>? TopSources = null,
    IReadOnlyList<ValueClicks>? TopMediums = null,
    IReadOnlyList<ValueClicks>? TopCampaigns = null,
    IReadOnlyList<ValueClicks>? Devices = null);

public sealed record DailyClicks(DateOnly Date, long Clicks);

public sealed record ReferrerClicks(string Referrer, long Clicks);

/// <summary>Count for one value of a dimension (UTM source, campaign, device class).</summary>
public sealed record ValueClicks(string Value, long Clicks);

/// <summary>Zero-filled daily series for charting (one entry per UTC day in [From, To]).</summary>
public sealed record DailyStatsResponse(string Code, DateOnly From, DateOnly To, long TotalClicks, IReadOnlyList<DailyClicks> Days);
