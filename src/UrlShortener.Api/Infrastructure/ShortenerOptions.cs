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
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public const string CreatePolicy = "create-link";

    [Range(1, 100_000)]
    public int CreatePermitLimit { get; set; } = 20;

    [Range(1, 3600)]
    public int CreateWindowSeconds { get; set; } = 60;
}
