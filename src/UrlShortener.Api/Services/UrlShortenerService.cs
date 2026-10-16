using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Api.Services;

/// <summary>Link lifecycle rules. Analytics reads live in <see cref="LinkAnalyticsService"/>; alias rules in <see cref="AliasPolicy"/>.</summary>
public sealed partial class UrlShortenerService(
    ILinkRepository repository,
    IShortCodeGenerator codeGenerator,
    IUrlValidator urlValidator,
    IEnumerable<IDestinationScreen> screens,
    IClickRecorder clickRecorder,
    IIdempotencyStore idempotency,
    TimeProvider timeProvider,
    IOptions<ShortenerOptions> options,
    ILogger<UrlShortenerService> logger) : IUrlShortenerService
{
    private readonly ShortenerOptions _options = options.Value;
    private readonly IDestinationScreen[] _screens = screens.ToArray();

    public async Task<CreateLinkResult> CreateAsync(
        CreateLinkRequest request, string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return new CreateLinkResult(await CreateCoreAsync(request, cancellationToken), Replayed: false);
        }

        var begin = idempotency.Begin(idempotencyKey, Fingerprint(request));
        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Replay:
                return new CreateLinkResult(await GetAsync(begin.Code!, cancellationToken), Replayed: true);
            case IdempotencyOutcome.Mismatch:
                throw new IdempotencyKeyMismatchException();
            case IdempotencyOutcome.InProgress:
                throw new IdempotencyKeyInProgressException();
        }

        try
        {
            var link = await CreateCoreAsync(request, cancellationToken);
            idempotency.Complete(idempotencyKey, link.Code);
            return new CreateLinkResult(link, Replayed: false);
        }
        catch
        {
            idempotency.Abandon(idempotencyKey);
            throw;
        }
    }

    public async Task<ShortLink> GetAsync(string code, CancellationToken cancellationToken = default) =>
        await repository.GetByCodeAsync(code, cancellationToken) ?? throw new LinkNotFoundException(code);

    public async Task<Uri> ResolveAsync(string code, ClickContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (link, now) = await ResolveUsableAsync(code, cancellationToken);
        await clickRecorder.RecordAsync(ClickEventFactory.Create(code, now, context), cancellationToken);
        return link.OriginalUrl;
    }

    public async Task<Uri> PeekAsync(string code, CancellationToken cancellationToken = default) =>
        (await ResolveUsableAsync(code, cancellationToken)).Link.OriginalUrl;

    public async Task DeactivateAsync(string code, CancellationToken cancellationToken = default)
    {
        var link = await GetAsync(code, cancellationToken);
        if (link.Deactivate(timeProvider.GetUtcNow()))
        {
            await repository.UpdateAsync(link, cancellationToken);
            LogDeactivated(logger, code);
        }
    }

    private async Task<ShortLink> CreateCoreAsync(CreateLinkRequest request, CancellationToken cancellationToken)
    {
        var target = urlValidator.Validate(request.Url);
        EnsureDestinationAllowed(target);

        var now = timeProvider.GetUtcNow();
        ValidateExpiry(request.ExpiresAt, now);

        return string.IsNullOrWhiteSpace(request.CustomAlias)
            ? await CreateWithGeneratedCodeAsync(target, now, request.ExpiresAt, cancellationToken)
            : await CreateWithAliasAsync(AliasPolicy.Normalize(request.CustomAlias), target, now, request.ExpiresAt, cancellationToken);
    }

    private async Task<(ShortLink Link, DateTimeOffset Now)> ResolveUsableAsync(string code, CancellationToken cancellationToken)
    {
        var link = await GetAsync(code, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (!link.IsActive)
        {
            throw new LinkGoneException(code, "deactivated");
        }

        if (link.IsExpired(now))
        {
            throw new LinkGoneException(code, "expired");
        }

        // Re-screen on every redirect: a domain added to the denylist later also stops existing links.
        if (FirstBlockReason(link.OriginalUrl) is not null)
        {
            LogBlockedRedirect(logger, code);
            throw new LinkGoneException(code, "blocked by safety policy");
        }

        return (link, now);
    }

    private void EnsureDestinationAllowed(Uri target)
    {
        if (FirstBlockReason(target) is { } reason)
        {
            throw new UnsafeDestinationException(reason);
        }
    }

    private string? FirstBlockReason(Uri target)
    {
        foreach (var screen in _screens)
        {
            if (screen.GetBlockReason(target) is { } reason)
            {
                return reason;
            }
        }

        return null;
    }

    private void ValidateExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not { } expiry)
        {
            return;
        }

        if (expiry <= now)
        {
            throw new InvalidExpiryException("ExpiresAt must be in the future.");
        }

        if (expiry > now.AddDays(_options.MaxExpiryDays))
        {
            throw new InvalidExpiryException($"ExpiresAt must be within {_options.MaxExpiryDays} days.");
        }
    }

    private async Task<ShortLink> CreateWithAliasAsync(
        string alias, Uri target, DateTimeOffset now, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        var link = new ShortLink(alias, target, now, expiresAt, isCustomAlias: true);
        if (!await repository.TryAddAsync(link, cancellationToken))
        {
            throw new AliasConflictException(alias);
        }

        LogCreated(logger, link.Code, true);
        return link;
    }

    private async Task<ShortLink> CreateWithGeneratedCodeAsync(
        Uri target, DateTimeOffset now, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        // Uniqueness is enforced by the atomic TryAdd, not by a check-then-insert (which races). See ADR-002.
        for (var attempt = 1; attempt <= _options.MaxCodeGenerationAttempts; attempt++)
        {
            var code = codeGenerator.Generate(_options.CodeLength);
            if (AliasPolicy.Reserved.Contains(code))
            {
                continue;
            }

            var link = new ShortLink(code, target, now, expiresAt, isCustomAlias: false);
            if (await repository.TryAddAsync(link, cancellationToken))
            {
                LogCreated(logger, code, false);
                return link;
            }

            LogCollision(logger, code, attempt);
        }

        throw new CodeGenerationException(_options.MaxCodeGenerationAttempts);
    }

    /// <summary>Stable hash of the semantic request, so a retried body with different whitespace/casing still matches.</summary>
    private static string Fingerprint(CreateLinkRequest request)
    {
        var canonical = string.Join(
            '\n',
            request.Url.Trim(),
            request.CustomAlias?.Trim() ?? string.Empty,
            request.ExpiresAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created short link {Code} (customAlias={IsCustom})")]
    private static partial void LogCreated(ILogger logger, string code, bool isCustom);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Short code collision on {Code}, attempt {Attempt}")]
    private static partial void LogCollision(ILogger logger, string code, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deactivated short link {Code}")]
    private static partial void LogDeactivated(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked redirect for {Code}: destination failed a safety screen")]
    private static partial void LogBlockedRedirect(ILogger logger, string code);
}
