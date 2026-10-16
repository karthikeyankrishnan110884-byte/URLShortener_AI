using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services;

/// <summary>
/// Blocks destinations whose host is, or is a subdomain of, a configured domain.
/// Uses <see cref="IOptionsMonitor{TOptions}"/> so a config reload takes effect without a restart.
/// </summary>
public sealed class DomainDenylistScreen : IDestinationScreen
{
    private volatile FrozenSet<string> _blocked;

    public DomainDenylistScreen(IOptionsMonitor<SafetyOptions> options)
    {
        _blocked = Build(options.CurrentValue);
        options.OnChange(o => _blocked = Build(o));
    }

    public string? GetBlockReason(Uri destination)
    {
        var blocked = _blocked;
        if (blocked.Count == 0)
        {
            return null;
        }

        // Walk "a.b.evil.com" → "b.evil.com" → "evil.com" → "com": O(labels) set lookups.
        var host = UrlValidator.NormalizeHost(destination.IdnHost);
        while (true)
        {
            if (blocked.Contains(host))
            {
                return $"The destination domain '{host}' is blocked by policy.";
            }

            var dot = host.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0)
            {
                return null;
            }

            host = host[(dot + 1)..];
        }
    }

    private static FrozenSet<string> Build(SafetyOptions options) =>
        options.BlockedDomains
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => UrlValidator.NormalizeHost(d.Trim()))
            .ToFrozenSet(StringComparer.Ordinal);
}
