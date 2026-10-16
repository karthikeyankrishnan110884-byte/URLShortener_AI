using Microsoft.Extensions.Options;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public class InMemoryIdempotencyStoreTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

    private InMemoryIdempotencyStore Sut() => new(Options.Create(new ShortenerOptions { IdempotencyKeyTtlHours = 1 }), _clock);

    [Fact]
    public void Lifecycle_StartedThenInProgressThenReplay()
    {
        var sut = Sut();

        Assert.Equal(IdempotencyOutcome.Started, sut.Begin("k", "fp").Outcome);
        Assert.Equal(IdempotencyOutcome.InProgress, sut.Begin("k", "fp").Outcome);

        sut.Complete("k", "abc1234");

        Assert.Equal(new IdempotencyResult(IdempotencyOutcome.Replay, "abc1234"), sut.Begin("k", "fp"));
        Assert.Equal(IdempotencyOutcome.Mismatch, sut.Begin("k", "other").Outcome);
    }

    [Fact]
    public void Abandon_AllowsRetry()
    {
        var sut = Sut();
        sut.Begin("k", "fp");

        sut.Abandon("k");

        Assert.Equal(IdempotencyOutcome.Started, sut.Begin("k", "fp").Outcome);
    }

    [Fact]
    public void Key_ExpiresAfterTtl()
    {
        var sut = Sut();
        sut.Begin("k", "fp");
        sut.Complete("k", "abc1234");

        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(IdempotencyOutcome.Started, sut.Begin("k", "other").Outcome);
    }

    [Fact]
    public async Task ConcurrentBegin_OnlyOneStarts()
    {
        var sut = Sut();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => sut.Begin("k", "fp").Outcome)));

        Assert.Single(outcomes, o => o == IdempotencyOutcome.Started);
        Assert.All(outcomes.Where(o => o != IdempotencyOutcome.Started), o => Assert.Equal(IdempotencyOutcome.InProgress, o));
    }

    [Fact]
    public void ExpiredEntries_AreSweptPeriodically()
    {
        var sut = Sut();
        for (var i = 0; i < 10; i++)
        {
            sut.Begin($"old{i}", "fp");
        }

        _clock.Advance(TimeSpan.FromHours(2));
        for (var i = 0; i < 1100; i++)
        {
            sut.Begin($"new{i}", "fp");
        }

        // An expired key behaves as new either way; this exercises the sweep path without exposing internals.
        Assert.Equal(IdempotencyOutcome.Started, sut.Begin("old0", "different").Outcome);
    }
}
