using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Contracts;

/// <summary>Request to shorten a URL.</summary>
public sealed class CreateLinkRequest
{
    /// <summary>Absolute http/https URL to shorten.</summary>
    /// <example>https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8</example>
    [Required]
    public string Url { get; init; } = string.Empty;

    /// <summary>Optional custom alias (3-32 chars: letters, digits, '-' or '_').</summary>
    /// <example>dotnet8</example>
    public string? CustomAlias { get; init; }

    /// <summary>Optional UTC expiry. Must be in the future.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
