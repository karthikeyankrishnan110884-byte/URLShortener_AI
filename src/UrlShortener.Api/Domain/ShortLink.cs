namespace UrlShortener.Api.Domain;

/// <summary>
/// A shortened link. State changes use atomic/volatile operations so the entity is safe to share between
/// concurrent redirects, the click processor and a deactivation request.
/// </summary>
public sealed class ShortLink
{
    private long _clickCount;
    private long _lastAccessedTicks;
    private long _deactivatedTicks;
    private int _isActive = 1;

    public ShortLink(string code, Uri originalUrl, DateTimeOffset createdAt, DateTimeOffset? expiresAt, bool isCustomAlias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(originalUrl);

        Code = code;
        OriginalUrl = originalUrl;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        IsCustomAlias = isCustomAlias;
    }

    public string Code { get; }
    public Uri OriginalUrl { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public bool IsCustomAlias { get; }

    /// <summary>Volatile read: a deactivation on one thread is visible to redirects on every other thread.</summary>
    public bool IsActive => Volatile.Read(ref _isActive) == 1;

    public DateTimeOffset? DeactivatedAt => FromTicks(Interlocked.Read(ref _deactivatedTicks));

    public long ClickCount => Interlocked.Read(ref _clickCount);

    public DateTimeOffset? LastAccessedAt => FromTicks(Interlocked.Read(ref _lastAccessedTicks));

    /// <summary>Expiry is exclusive: a link expiring at T is no longer usable at T.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && now >= expiry;

    public bool IsUsable(DateTimeOffset now) => IsActive && !IsExpired(now);

    /// <summary>Soft-deactivates the link. Idempotent and race-free: only the first caller records the time.</summary>
    /// <returns><c>true</c> if this call changed the state.</returns>
    public bool Deactivate(DateTimeOffset now)
    {
        if (Interlocked.CompareExchange(ref _isActive, 0, 1) != 1)
        {
            return false;
        }

        Interlocked.Exchange(ref _deactivatedTicks, now.UtcTicks);
        return true;
    }

    public void RegisterClick(DateTimeOffset now) => RegisterClicks(1, now);

    /// <summary>Applies a batch of clicks in one step (the processor groups events per link).</summary>
    public void RegisterClicks(long count, DateTimeOffset latest)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Interlocked.Add(ref _clickCount, count);

        // Keep the latest timestamp even if events are processed out of order.
        long seen;
        do
        {
            seen = Interlocked.Read(ref _lastAccessedTicks);
            if (latest.UtcTicks <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _lastAccessedTicks, latest.UtcTicks, seen) != seen);
    }

    private static DateTimeOffset? FromTicks(long ticks) => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
}
