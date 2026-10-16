using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Infrastructure;

public sealed class ShortenerOptions
{
    public const string SectionName = "Shortener";

    /// <summary>Public base URL used to build short links.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "http://localhost:5104";

    [Range(5, 16)]
    public int CodeLength { get; set; } = 7;

    [Range(1, 20)]
    public int MaxCodeGenerationAttempts { get; set; } = 5;

    [Range(32, 8192)]
    public int MaxUrlLength { get; set; } = 2048;

    /// <summary>Upper bound for <c>expiresAt</c>, so links cannot be created with absurd lifetimes.</summary>
    [Range(1, 36_500)]
    public int MaxExpiryDays { get; set; } = 3650;

    /// <summary>How long an Idempotency-Key is remembered.</summary>
    [Range(1, 168)]
    public int IdempotencyKeyTtlHours { get; set; } = 24;
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string CreatePolicy = "create-link";
    public const string RedirectPolicy = "redirect";

    [Range(1, 100_000)]
    public int CreatePermitLimit { get; set; } = 20;

    [Range(1, 3600)]
    public int CreateWindowSeconds { get; set; } = 60;

    /// <summary>Generous per-IP redirect limit that slows code enumeration and floods. 0 disables it (load tests).</summary>
    [Range(0, 1_000_000)]
    public int RedirectPermitLimit { get; set; } = 600;

    [Range(1, 3600)]
    public int RedirectWindowSeconds { get; set; } = 60;
}

public enum AnalyticsMode
{
    /// <summary>Clicks are queued and written by a background worker (default; keeps redirects fast).</summary>
    Async,

    /// <summary>Clicks are written on the request thread (deterministic; used for comparison and some tests).</summary>
    Inline,
}

public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    public AnalyticsMode Mode { get; set; } = AnalyticsMode.Async;

    /// <summary>Bounded queue between redirects and the writer. When full, new events are dropped and counted.</summary>
    [Range(100, 10_000_000)]
    public int QueueCapacity { get; set; } = 50_000;

    /// <summary>Max events written per storage round-trip by the background processor.</summary>
    [Range(1, 100_000)]
    public int MaxBatchSize { get; set; } = 1_000;

    /// <summary>Raw events kept per link (newest first wins). Aggregates stay exact regardless.</summary>
    [Range(0, 1_000_000)]
    public int MaxRecentClicksPerLink { get; set; } = 1_000;

    /// <summary>Distinct referrers/campaigns tracked per link; overflow is grouped as "(other)".</summary>
    [Range(10, 100_000)]
    public int MaxDistinctValuesPerDimension { get; set; } = 500;

    /// <summary>Largest range accepted by the daily stats endpoint.</summary>
    [Range(1, 3660)]
    public int MaxDailyRangeDays { get; set; } = 366;
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public bool Enabled { get; set; } = true;

    /// <summary>Upper bound on staleness for other nodes after a deactivation (see ADR-005).</summary>
    [Range(1, 86_400)]
    public int LinkTtlSeconds { get; set; } = 60;

    /// <summary>Unknown codes are cached briefly so 404 floods don't reach storage. 0 disables.</summary>
    [Range(0, 3600)]
    public int NotFoundTtlSeconds { get; set; } = 5;

    [Range(100, 10_000_000)]
    public int SizeLimit { get; set; } = 100_000;
}

public sealed class SafetyOptions
{
    public const string SectionName = "Safety";

    /// <summary>Blocked registrable domains; subdomains are blocked too. Swap for a threat-intel feed in production.</summary>
    public List<string> BlockedDomains { get; set; } = [];
}

/// <summary>Developer-only knob to emulate a remote store so cache and async-analytics effects are measurable.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Range(0, 1000)]
    public int SimulatedLatencyMs { get; set; }
}

public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>Trust X-Forwarded-For only from these proxies, so the per-IP limits see the real client.</summary>
    public List<string> KnownProxies { get; set; } = [];
}
