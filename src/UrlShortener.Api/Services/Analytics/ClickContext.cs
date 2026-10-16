using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>Raw, untrusted request data captured at redirect time. Normalised by <see cref="ClickEventFactory"/>.</summary>
public sealed record ClickContext(string? Referrer, string? UserAgent, string? UtmSource = null, string? UtmMedium = null, string? UtmCampaign = null)
{
    public static readonly ClickContext Empty = new(null, null);
}

/// <summary>Turns untrusted request data into a bounded, privacy-preserving <see cref="ClickEvent"/>.</summary>
internal static class ClickEventFactory
{
    internal const int MaxUserAgentLength = 256;
    internal const int MaxTagLength = 64;

    public static ClickEvent Create(string code, DateTimeOffset now, ClickContext context)
    {
        var userAgent = Truncate(context.UserAgent, MaxUserAgentLength);
        var utm = new UtmTags(Tag(context.UtmSource), Tag(context.UtmMedium), Tag(context.UtmCampaign));

        return new ClickEvent(
            code,
            now,
            NormalizeReferrer(context.Referrer),
            userAgent,
            utm.IsEmpty ? null : utm,
            UserAgentClassifier.Classify(userAgent));
    }

    /// <summary>Only the referrer host is kept (no path/query, which can contain personal data).</summary>
    internal static string? NormalizeReferrer(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;

    /// <summary>UTM values: trimmed, lower-cased, max 64 chars, restricted charset. Anything else is dropped.</summary>
    internal static string? Tag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim().ToLowerInvariant();
        if (value.Length > MaxTagLength)
        {
            value = value[..MaxTagLength];
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '+' or ' '))
            {
                return null;
            }
        }

        return value;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
