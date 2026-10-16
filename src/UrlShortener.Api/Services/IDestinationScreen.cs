namespace UrlShortener.Api.Services;

/// <summary>
/// Extension point for destination reputation checks (denylist, Google Safe Browsing, internal threat intel).
/// All registered screens run at create time and again at redirect time, so a domain blocked later
/// also stops existing links.
/// </summary>
public interface IDestinationScreen
{
    /// <summary>Returns a human-readable reason when the destination must be blocked, otherwise <c>null</c>.</summary>
    string? GetBlockReason(Uri destination);
}
