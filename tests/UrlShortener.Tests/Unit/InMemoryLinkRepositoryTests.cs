using UrlShortener.Api.Domain;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Tests.Unit;

public class InMemoryLinkRepositoryTests
{
    private static ShortLink Link(string code, string url = "https://example.com") =>
        new(code, new Uri(url), DateTimeOffset.UtcNow, null, isCustomAlias: false);

    [Fact]
    public async Task TryAdd_RejectsDuplicateCode_AndKeepsOriginalLink()
    {
        var repo = new InMemoryLinkRepository();

        Assert.True(await repo.TryAddAsync(Link("dup", "https://first.example")));
        Assert.False(await repo.TryAddAsync(Link("dup", "https://second.example")));

        Assert.Equal("https://first.example/", (await repo.GetByCodeAsync("dup"))!.OriginalUrl.AbsoluteUri);
    }

    [Fact]
    public async Task TryAdd_UnderConcurrentWritesOfSameCode_ExactlyOneWins()
    {
        // Regression guard for the brownfield bug "two URLs occasionally get the same code".
        var repo = new InMemoryLinkRepository();

        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(i => Task.Run(async () => await repo.TryAddAsync(Link("race", $"https://site{i}.example")))));

        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task Codes_AreCaseSensitive()
    {
        var repo = new InMemoryLinkRepository();
        await repo.TryAddAsync(Link("AbC"));

        Assert.NotNull(await repo.GetByCodeAsync("AbC"));
        Assert.Null(await repo.GetByCodeAsync("abc"));
    }

    [Fact]
    public async Task Update_PersistsState()
    {
        var repo = new InMemoryLinkRepository();
        var link = Link("upd");
        await repo.TryAddAsync(link);

        link.Deactivate(DateTimeOffset.UtcNow);
        await repo.UpdateAsync(link);

        Assert.False((await repo.GetByCodeAsync("upd"))!.IsActive);
    }
}
