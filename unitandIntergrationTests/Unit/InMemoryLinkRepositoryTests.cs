using UrlShortener.Api.Domain;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Tests.Unit;

public class InMemoryLinkRepositoryTests
{
    private static ShortLink Link(string code, string url = "https://example.com") =>
        new(code, new Uri(url), DateTimeOffset.UtcNow, null, isCustomAlias: false);

    [Fact]
    public void TryAdd_RejectsDuplicateCode_AndKeepsOriginalLink()
    {
        var repo = new InMemoryLinkRepository();

        Assert.True(repo.TryAdd(Link("dup", "https://first.example")));
        Assert.False(repo.TryAdd(Link("dup", "https://second.example")));

        Assert.Equal("https://first.example/", repo.GetByCode("dup")!.OriginalUrl.AbsoluteUri);
    }

    [Fact]
    public async Task TryAdd_UnderConcurrentWritesOfSameCode_ExactlyOneWins()
    {
        // Regression guard for the brownfield bug "two URLs occasionally get the same code".
        var repo = new InMemoryLinkRepository();

        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(i => Task.Run(() => repo.TryAdd(Link("race", $"https://site{i}.example")))));

        Assert.Single(results, r => r);
    }

    [Fact]
    public void Codes_AreCaseSensitive()
    {
        var repo = new InMemoryLinkRepository();
        repo.TryAdd(Link("AbC"));

        Assert.NotNull(repo.GetByCode("AbC"));
        Assert.Null(repo.GetByCode("abc"));
    }

    [Fact]
    public void GetClicks_ReturnsOnlyClicksForThatCode()
    {
        var repo = new InMemoryLinkRepository();
        repo.AddClick(new ClickEvent("a", DateTimeOffset.UtcNow, null, null));
        repo.AddClick(new ClickEvent("a", DateTimeOffset.UtcNow, null, null));
        repo.AddClick(new ClickEvent("b", DateTimeOffset.UtcNow, null, null));

        Assert.Equal(2, repo.GetClicks("a").Count);
        Assert.Empty(repo.GetClicks("missing"));
    }
}
