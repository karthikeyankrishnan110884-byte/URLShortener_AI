namespace UrlShortener.Api.Domain;

/// <summary>A shortened link. Click counters are updated atomically so the entity is safe under concurrent redirects.</summary>
public sealed class ShortLink
{
    private long _clickCount;
    private long _lastAccessedTicks;

    public ShortLink(string code, Uri originalUrl, DateTimeOffset createdAt, DateTimeOffset? expiresAt, bool isCustomAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(originalUrl);

        Code = code;
        OriginalUrl = originalUrl;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsCustomAlias = isCustomAlias;
        IsActive = true;
    }

    public string Code { get; }
    public Uri OriginalUrl { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public bool IsCustomAlias { get; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? DeactivatedAt { get; private set; }

    public long ClickCount => Interlocked.Read(ref _clickCount);

    public DateTimeOffset? LastAccessedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastAccessedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Expiry is exclusive: a link expiring at T is no longer usable at T.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && now >= expiry;

    public bool IsUsable(DateTimeOffset now) => IsActive && !IsExpired(now);

    /// <summary>Soft-deactivates the link. Idempotent: the first deactivation time is kept.</summary>
    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAt = now;
    }

    public void RegisterClick(DateTimeOffset now)
    {
        Interlocked.Increment(ref _clickCount);
        Interlocked.Exchange(ref _lastAccessedTicks, now.UtcTicks);
    }
}
