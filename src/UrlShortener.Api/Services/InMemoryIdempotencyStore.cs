using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Services;

/// <summary>In-memory idempotency store with TTL. Production: Redis <c>SET NX PX</c> or a SQL table keyed by (client, key).</summary>
public sealed class InMemoryIdempotencyStore(IOptions<ShortenerOptions> options, TimeProvider timeProvider) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl = TimeSpan.FromHours(options.Value.IdempotencyKeyTtlHours);
    private int _operations;

    public IdempotencyResult Begin(string key, string fingerprint)
    {
        var now = timeProvider.GetUtcNow();
        SweepOccasionally(now);

        while (true)
        {
            var pending = new Entry(fingerprint, null, now + _ttl);
            if (_entries.TryAdd(key, pending))
            {
                return new(IdempotencyOutcome.Started);
            }

            if (!_entries.TryGetValue(key, out var existing))
            {
                continue; // removed concurrently; retry
            }

            if (existing.ExpiresAt <= now)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, existing));
                continue;
            }

            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new(IdempotencyOutcome.Mismatch);
            }

            return existing.Code is null
                ? new(IdempotencyOutcome.InProgress)
                : new(IdempotencyOutcome.Replay, existing.Code);
        }
    }

    public void Complete(string key, string code)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            _entries[key] = entry with { Code = code };
        }
    }

    public void Abandon(string key) => _entries.TryRemove(key, out _);

    private void SweepOccasionally(DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _operations) % 1024 != 0)
        {
            return;
        }

        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAt <= now)
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
        }
    }

    private sealed record Entry(string Fingerprint, string? Code, DateTimeOffset ExpiresAt);
}
