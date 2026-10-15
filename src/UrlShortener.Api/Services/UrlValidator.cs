using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services;

/// <summary>
/// Guards against abuse: only absolute http/https URLs, bounded length, no embedded credentials,
/// and no loopback/private/link-local targets (prevents the service being used to reach internal hosts).
/// </summary>
public sealed class UrlValidator(IOptions<ShortenerOptions> options) : IUrlValidator
{
    private readonly ShortenerOptions _options = options.Value;

    public Uri Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidUrlException("URL is required.");
        }

        url = url.Trim();

        if (url.Length > _options.MaxUrlLength)
        {
            throw new InvalidUrlException($"URL must be at most {_options.MaxUrlLength} characters.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidUrlException("URL must be absolute, e.g. https://example.com/page.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidUrlException("Only http and https URLs are allowed.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidUrlException("URLs containing credentials are not allowed.");
        }

        if (IsInternalHost(uri))
        {
            throw new InvalidUrlException("URLs pointing to local or private network addresses are not allowed.");
        }

        return uri;
    }

    private static bool IsInternalHost(Uri uri)
    {
        var host = uri.IdnHost;

        if (uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
        {
            // Hostname: DNS is not resolved here (see RISKS.md: DNS rebinding is out of scope for the prototype).
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
        }

        var b = ip.GetAddressBytes();
        return b[0] == 10                                   // 10.0.0.0/8
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
            || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 (cloud metadata)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 (CGNAT)
            || b[0] == 0;                                   // 0.0.0.0/8
    }
}
