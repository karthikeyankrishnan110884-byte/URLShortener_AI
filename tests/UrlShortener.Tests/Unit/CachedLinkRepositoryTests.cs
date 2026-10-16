using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public sealed class CachedLinkRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly CountingLinkRepository _inner = new(new InMemoryLinkRepository());
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly MemoryCache _cache;
    private readonly CachedLinkRepository _sut;

    public CachedLinkRepositoryTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1000, Clock = new ClockAdapter(_clock) });
        _sut = new CachedLinkRepository(_inner, _cache, Options.Create(new CacheOptions { LinkTtlSeconds = 60, NotFoundTtlSeconds = 5 }), _clock);
    }

    public void Dispose() => _cache.Dispose();

    private static ShortLink Link(string code, DateTimeOffset? expiresAt = null) =>
        new(code, new Uri("https://example.com"), Now, expiresAt, isCustomAlias: false);

    [Fact]
    public async Task RepeatedReads_HitStorageOnce()
    {
        await _sut.TryAddAsync(Link("hot"));

        for (var i = 0; i < 10; i++)
        {
            Assert.NotNull(await _sut.GetByCodeAsync("hot"));
        }

        Assert.Equal(1, _inner.Reads);
    }

    [Fact]
    public async Task Entry_ExpiresAfterTtl()
    {
        await _sut.TryAddAsync(Link("ttl"));
        await _sut.GetByCodeAsync("ttl");

        _clock.Advance(TimeSpan.FromSeconds(61));
        await _sut.GetByCodeAsync("ttl");

        Assert.Equal(2, _inner.Reads);
    }

    [Fact]
    public async Task Entry_NeverOutlivesTheLinksOwnExpiry()
    {
        await _sut.TryAddAsync(Link("short", expiresAt: Now.AddSeconds(10)));
        await _sut.GetByCodeAsync("short");

        _clock.Advance(TimeSpan.FromSeconds(11));
        await _sut.GetByCodeAsync("short");

        Assert.Equal(2, _inner.Reads);
    }

    [Fact]
    public async Task Update_InvalidatesEntry()
    {
        var link = Link("deact");
        await _sut.TryAddAsync(link);
        await _sut.GetByCodeAsync("deact");

        link.Deactivate(Now);
        await _sut.UpdateAsync(link);
        var reloaded = await _sut.GetByCodeAsync("deact");

        Assert.Equal(2, _inner.Reads);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task UnknownCode_IsNegativelyCached_UntilAdded()
    {
        Assert.Null(await _sut.GetByCodeAsync("later"));
        Assert.Null(await _sut.GetByCodeAsync("later"));
        Assert.Equal(1, _inner.Reads);

        await _sut.TryAddAsync(Link("later"));

        Assert.NotNull(await _sut.GetByCodeAsync("later"));
    }

    [Fact]
    public async Task ConcurrentMisses_ForSameKey_AreCoalescedIntoOneLoad()
    {
        await _inner.TryAddAsync(Link("stampede"));
        _inner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var readers = Enumerable.Range(0, 50).Select(_ => _sut.GetByCodeAsync("stampede").AsTask()).ToArray();
        await Task.Delay(50);
        _inner.Gate.SetResult();
        var results = await Task.WhenAll(readers);

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(1, _inner.Reads);
    }

    private sealed class ClockAdapter(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
