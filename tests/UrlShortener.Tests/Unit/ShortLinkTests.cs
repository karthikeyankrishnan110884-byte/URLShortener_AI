using UrlShortener.Api.Domain;

namespace UrlShortener.Tests.Unit;

public class ShortLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static ShortLink NewLink(DateTimeOffset? expiresAt = null) =>
        new("abc1234", new Uri("https://example.com"), Now, expiresAt, isCustomAlias: false);

    [Fact]
    public void Link_WithoutExpiry_NeverExpires()
    {
        Assert.False(NewLink().IsExpired(Now.AddYears(100)));
    }

    [Fact]
    public void IsExpired_IsExclusiveAtTheExpiryInstant()
    {
        var link = NewLink(expiresAt: Now.AddHours(1));

        Assert.False(link.IsExpired(Now.AddHours(1).AddTicks(-1)));
        Assert.True(link.IsExpired(Now.AddHours(1)));
    }

    [Fact]
    public void Deactivate_IsIdempotent_AndKeepsFirstTimestamp()
    {
        var link = NewLink();

        Assert.True(link.Deactivate(Now));
        Assert.False(link.Deactivate(Now.AddDays(1)));

        Assert.False(link.IsActive);
        Assert.False(link.IsUsable(Now));
        Assert.Equal(Now, link.DeactivatedAt);
    }

    [Fact]
    public async Task Deactivate_UnderConcurrency_ExactlyOneCallerWins()
    {
        var link = NewLink();

        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(i => Task.Run(() => link.Deactivate(Now.AddSeconds(i)))));

        Assert.Single(results, r => r);
        Assert.False(link.IsActive);
    }

    [Fact]
    public async Task RegisterClick_CountsEveryClick_UnderConcurrency()
    {
        var link = NewLink();

        await Task.WhenAll(Enumerable.Range(0, 1_000).Select(_ => Task.Run(() => link.RegisterClick(Now))));

        Assert.Equal(1_000, link.ClickCount);
        Assert.Equal(Now, link.LastAccessedAt);
    }

    [Fact]
    public void RegisterClick_OutOfOrder_KeepsLatestTimestamp()
    {
        var link = NewLink();

        link.RegisterClick(Now.AddMinutes(5));
        link.RegisterClick(Now);

        Assert.Equal(Now.AddMinutes(5), link.LastAccessedAt);
        Assert.Equal(2, link.ClickCount);
    }
}
