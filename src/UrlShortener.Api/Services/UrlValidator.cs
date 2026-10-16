using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services;

/// <summary>
/// Guards against abuse: only absolute http/https URLs, bounded length, no embedded credentials,
/// no links back to this service (redirect loops), and no loopback/private/link-local/multicast targets
/// (prevents the service being used as an open redirect into internal networks).
/// </summary>
/// <remarks>
/// Encoded IPv4 forms (<c>2130706433</c>, <c>0x7f000001</c>, <c>127.1</c>) are canonicalised by <see cref="Uri"/>
/// before these checks, and are covered by tests. DNS names are not resolved here (see RISKS.md, DNS rebinding).
/// </remarks>
public sealed class UrlValidator(IOptions<ShortenerOptions> options) : IUrlValidator
{
    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".lan", ".home.arpa", ".intranet"];

    private readonly ShortenerOptions _options = options.Value;
    private readonly string? _ownHost = Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var b) ? NormalizeHost(b.IdnHost) : null;

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

        // On Linux/macOS "/relative/path" parses as an absolute file:// URI, so implicit file paths are rejected explicitly.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.IsFile && !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
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

        if (_ownHost is not null && NormalizeHost(uri.IdnHost) == _ownHost)
        {
            throw new InvalidUrlException("Links to this shortener are not allowed (redirect loop).");
        }

        return uri;
    }

    /// <summary>Lower-case, without the trailing root dot ("localhost." is "localhost").</summary>
    internal static string NormalizeHost(string host) => host.TrimEnd('.').ToLowerInvariant();

    private static bool IsInternalHost(Uri uri)
    {
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            // Uri.Host drops any IPv6 zone id ("%eth0"), which would otherwise defeat IPAddress.TryParse.
            return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IsInternalAddress(ip);
        }

        var host = NormalizeHost(uri.IdnHost);
        if (host.Length == 0 || host == "localhost" || !host.Contains('.', StringComparison.Ordinal))
        {
            // Single-label names ("intranet", "printer") only resolve inside private networks.
            return true;
        }

        return InternalSuffixes.Any(s => host.EndsWith(s, StringComparison.Ordinal));
    }

    internal static bool IsInternalAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var v6 = ip.GetAddressBytes();
            if (TryGetEmbeddedIPv4(v6, out var embedded))
            {
                return IsInternalAddress(embedded);
            }

            return IPAddress.IsLoopback(ip)
                || ip.Equals(IPAddress.IPv6Any)
                || ip.IsIPv6LinkLocal
                || ip.IsIPv6SiteLocal
                || ip.IsIPv6UniqueLocal
                || ip.IsIPv6Multicast;
        }

        var b = ip.GetAddressBytes();
        return b[0] == 0                                    // 0.0.0.0/8 "this network"
            || b[0] == 10                                   // 10.0.0.0/8
            || b[0] == 127                                  // 127.0.0.0/8 loopback
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 CGNAT
            || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link-local / cloud metadata
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // 192.0.0.0/24 IETF protocol assignments
            || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
            || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // 198.18.0.0/15 benchmarking
            || b[0] >= 224;                                 // multicast, reserved, broadcast
    }

    /// <summary>NAT64 (64:ff9b::/96) and 6to4 (2002::/16) addresses carry an IPv4 address that must be checked too.</summary>
    private static bool TryGetEmbeddedIPv4(byte[] v6, out IPAddress embedded)
    {
        if (v6[0] == 0x00 && v6[1] == 0x64 && v6[2] == 0xff && v6[3] == 0x9b && v6.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
        {
            embedded = new IPAddress(v6.AsSpan(12, 4));
            return true;
        }

        if (v6[0] == 0x20 && v6[1] == 0x02)
        {
            embedded = new IPAddress(v6.AsSpan(2, 4));
            return true;
        }

        embedded = IPAddress.None;
        return false;
    }
}
