namespace UrlShortener.Api.Domain;

/// <summary>Pre-aggregated click analytics for one link (maintained on write, so reads are O(dimensions) not O(clicks)).</summary>
public sealed record ClickSummary(
    long Total,
    IReadOnlyDictionary<DateOnly, long> ByDay,
    IReadOnlyDictionary<string, long> ByReferrer,
    IReadOnlyDictionary<string, long> BySource,
    IReadOnlyDictionary<string, long> ByMedium,
    IReadOnlyDictionary<string, long> ByCampaign,
    IReadOnlyDictionary<string, long> ByDevice)
{
    /// <summary>Bucket for values beyond the per-dimension cardinality cap.</summary>
    public const string Other = "(other)";

    /// <summary>Referrer bucket for clicks without a (valid) referrer.</summary>
    public const string Direct = "(direct)";

    /// <summary>UTM bucket for untagged clicks.</summary>
    public const string None = "(none)";

    public static readonly ClickSummary Empty = new(
        0,
        new Dictionary<DateOnly, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>(),
        new Dictionary<string, long>());
}
