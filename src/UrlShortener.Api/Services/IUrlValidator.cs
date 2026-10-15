namespace UrlShortener.Api.Services;

public interface IUrlValidator
{
    /// <summary>Validates and normalizes a target URL. Throws <see cref="Domain.InvalidUrlException"/> when unsafe or malformed.</summary>
    Uri Validate(string? url);
}
