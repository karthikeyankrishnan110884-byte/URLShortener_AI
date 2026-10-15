using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Api.Services;

public sealed partial class UrlShortenerService(
    ILinkRepository repository,
    IShortCodeGenerator codeGenerator,
    IUrlValidator urlValidator,
    TimeProvider timeProvider,
    IOptions<ShortenerOptions> options,
    ILogger<UrlShortenerService> logger) : IUrlShortenerService
{
    /// <summary>Aliases that would shadow API/infra routes.</summary>
    private static readonly HashSet<string> ReservedAliases =
        new(["api", "swagger", "health", "admin", "favicon.ico"], StringComparer.OrdinalIgnoreCase);

    private const int MaxUserAgentLength = 256;

    private readonly ShortenerOptions _options = options.Value;

    public ShortLink Create(CreateLinkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = urlValidator.Validate(request.Url);
        var now = timeProvider.GetUtcNow();

        if (request.ExpiresAt is { } expiresAt && expiresAt <= now)
        {
            throw new InvalidExpiryException("ExpiresAt must be in the future.");
        }

        return string.IsNullOrWhiteSpace(request.CustomAlias)
            ? CreateWithGeneratedCode(target, now, request.ExpiresAt)
            : CreateWithAlias(request.CustomAlias.Trim(), target, now, request.ExpiresAt);
    }

    public ShortLink Get(string code) =>
        repository.GetByCode(code) ?? throw new LinkNotFoundException(code);

    public Uri ResolveAndTrack(string code, string? referrer, string? userAgent)
    {
        var link = Get(code);
        var now = timeProvider.GetUtcNow();

        if (!link.IsActive)
        {
            throw new LinkGoneException(code, "deactivated");
        }

        if (link.IsExpired(now))
        {
            throw new LinkGoneException(code, "expired");
        }

        link.RegisterClick(now);
        repository.AddClick(new ClickEvent(code, now, NormalizeReferrer(referrer), Truncate(userAgent, MaxUserAgentLength)));

        return link.OriginalUrl;
    }

    public LinkStatsResponse GetStats(string code, int topReferrers = 5)
    {
        var link = Get(code);
        var clicks = repository.GetClicks(code);

        var byDay = clicks
            .GroupBy(c => DateOnly.FromDateTime(c.OccurredAt.UtcDateTime))
            .Select(g => new DailyClicks(g.Key, g.Count()))
            .OrderBy(d => d.Date)
            .ToList();

        var referrers = clicks
            .GroupBy(c => c.ReferrerHost ?? "(direct)", StringComparer.OrdinalIgnoreCase)
            .Select(g => new ReferrerClicks(g.Key, g.Count()))
            .OrderByDescending(r => r.Clicks)
            .ThenBy(r => r.Referrer, StringComparer.Ordinal)
            .Take(topReferrers)
            .ToList();

        return new LinkStatsResponse(link.Code, link.ClickCount, link.LastAccessedAt, byDay, referrers);
    }

    public void Deactivate(string code)
    {
        var link = Get(code);
        link.Deactivate(timeProvider.GetUtcNow());
        LogDeactivated(logger, code);
    }

    private ShortLink CreateWithAlias(string alias, Uri target, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        if (!AliasPattern().IsMatch(alias))
        {
            throw new InvalidAliasException("Alias must be 3-32 characters: letters, digits, '-' or '_'.");
        }

        if (ReservedAliases.Contains(alias))
        {
            throw new InvalidAliasException($"The alias '{alias}' is reserved.");
        }

        var link = new ShortLink(alias, target, now, expiresAt, isCustomAlias: true);
        if (!repository.TryAdd(link))
        {
            throw new AliasConflictException(alias);
        }

        LogCreated(logger, link.Code, true);
        return link;
    }

    private ShortLink CreateWithGeneratedCode(Uri target, DateTimeOffset now, DateTimeOffset? expiresAt)
    {
        // Uniqueness is enforced by the atomic TryAdd, not by a check-then-insert (which races).
        for (var attempt = 1; attempt <= _options.MaxCodeGenerationAttempts; attempt++)
        {
            var code = codeGenerator.Generate(_options.CodeLength);
            var link = new ShortLink(code, target, now, expiresAt, isCustomAlias: false);

            if (repository.TryAdd(link))
            {
                LogCreated(logger, code, false);
                return link;
            }

            LogCollision(logger, code, attempt);
        }

        throw new CodeGenerationException(_options.MaxCodeGenerationAttempts);
    }

    private static string? NormalizeReferrer(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    [GeneratedRegex("^[A-Za-z0-9_-]{3,32}$")]
    private static partial Regex AliasPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "Created short link {Code} (customAlias={IsCustom})")]
    private static partial void LogCreated(ILogger logger, string code, bool isCustom);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Short code collision on {Code}, attempt {Attempt}")]
    private static partial void LogCollision(ILogger logger, string code, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deactivated short link {Code}")]
    private static partial void LogDeactivated(ILogger logger, string code);
}
