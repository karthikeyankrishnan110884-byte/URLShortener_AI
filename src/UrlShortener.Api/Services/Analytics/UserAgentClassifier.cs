namespace UrlShortener.Api.Services.Analytics;

/// <summary>
/// Coarse device classification ("user-agent summary"). Deliberately simple and dependency-free;
/// a UA-parsing library (or client hints) is the production upgrade.
/// </summary>
public static class UserAgentClassifier
{
    public const string Bot = "bot";
    public const string Mobile = "mobile";
    public const string Tablet = "tablet";
    public const string Desktop = "desktop";
    public const string Unknown = "unknown";

    private static readonly string[] BotMarkers =
        ["bot", "crawler", "spider", "slurp", "facebookexternalhit", "preview", "curl/", "wget/", "python-requests", "httpclient", "headless"];

    public static string Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return Unknown;
        }

        var ua = userAgent.ToLowerInvariant();

        if (BotMarkers.Any(m => ua.Contains(m, StringComparison.Ordinal)))
        {
            return Bot;
        }

        if (ua.Contains("ipad", StringComparison.Ordinal) || ua.Contains("tablet", StringComparison.Ordinal)
            || (ua.Contains("android", StringComparison.Ordinal) && !ua.Contains("mobile", StringComparison.Ordinal)))
        {
            return Tablet;
        }

        if (ua.Contains("mobi", StringComparison.Ordinal) || ua.Contains("iphone", StringComparison.Ordinal) || ua.Contains("android", StringComparison.Ordinal))
        {
            return Mobile;
        }

        if (ua.Contains("windows", StringComparison.Ordinal) || ua.Contains("macintosh", StringComparison.Ordinal)
            || ua.Contains("x11", StringComparison.Ordinal) || ua.Contains("linux", StringComparison.Ordinal) || ua.Contains("cros", StringComparison.Ordinal))
        {
            return Desktop;
        }

        return Unknown;
    }
}
