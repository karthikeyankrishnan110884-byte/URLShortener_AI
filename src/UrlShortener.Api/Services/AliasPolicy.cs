using System.Text.RegularExpressions;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Services;

/// <summary>Rules for user-chosen aliases (extracted from the service for single responsibility).</summary>
internal static partial class AliasPolicy
{
    /// <summary>Aliases that would shadow API/infra routes. Compared case-insensitively.</summary>
    internal static readonly IReadOnlySet<string> Reserved =
        new HashSet<string>(["api", "swagger", "health", "healthz", "readyz", "metrics", "admin", "static", "docs"], StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string alias)
    {
        alias = alias.Trim();

        if (!Pattern().IsMatch(alias))
        {
            throw new InvalidAliasException("Alias must be 3-32 characters: letters, digits, '-' or '_'.");
        }

        if (Reserved.Contains(alias))
        {
            throw new InvalidAliasException($"The alias '{alias}' is reserved.");
        }

        return alias;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    private static partial Regex Pattern();
}
