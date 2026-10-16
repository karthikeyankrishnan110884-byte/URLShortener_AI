using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Tests.Unit;

public class InMemoryClickRepositoryTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 10, 10, 23, 59, 0, TimeSpan.Zero);

    private static InMemoryClickRepository Repo(int recent = 1000, int distinct = 500) =>
        new(Options.Create(new AnalyticsOptions { MaxRecentClicksPerLink = recent, MaxDistinctValuesPerDimension = distinct }));

    [Fact]
    public async Task Summary_IsIsolatedPerCode_AndEmptyForUnknownCode()
    {
        var repo = Repo();
        await repo.AddAsync(new ClickEvent("a", Day1, null, null));
        await repo.AddAsync(new ClickEvent("a", Day1, null, null));
        await repo.AddAsync(new ClickEvent("b", Day1, null, null));

        Assert.Equal(2, (await repo.GetSummaryAsync("a")).Total);
        Assert.Equal(0, (await repo.GetSummaryAsync("missing")).Total);
        Assert.Empty(await repo.GetRecentAsync("missing"));
    }

    [Fact]
    public async Task Summary_AggregatesByUtcDay_ReferrerUtmAndDevice()
    {
        var repo = Repo();
        await repo.AddAsync(new ClickEvent("a", Day1, "google.com", null, new UtmTags("newsletter", "email", "spring"), "mobile"));
        await repo.AddAsync(new ClickEvent("a", Day1.AddMinutes(2), "GOOGLE.com", null, null, "desktop")); // next UTC day
        await repo.AddAsync(new ClickEvent("a", Day1, null, null));

        var s = await repo.GetSummaryAsync("a");

        Assert.Equal(3, s.Total);
        Assert.Equal(2, s.ByDay[new DateOnly(2026, 10, 10)]);
        Assert.Equal(1, s.ByDay[new DateOnly(2026, 10, 11)]);
        Assert.Equal(2, s.ByReferrer["google.com"]);
        Assert.Equal(1, s.ByReferrer[ClickSummary.Direct]);
        Assert.Equal(1, s.BySource["newsletter"]);
        Assert.Equal(2, s.BySource[ClickSummary.None]);
        Assert.Equal(1, s.ByCampaign["spring"]);
        Assert.Equal(1, s.ByMedium["email"]);
        Assert.Equal(1, s.ByDevice["unknown"]);
    }

    [Fact]
    public async Task RecentEvents_AreBounded_WhileTotalsStayExact()
    {
        var repo = Repo(recent: 10);

        for (var i = 0; i < 25; i++)
        {
            await repo.AddAsync(new ClickEvent("a", Day1.AddSeconds(i), null, null));
        }

        var recent = await repo.GetRecentAsync("a");
        Assert.Equal(10, recent.Count);
        Assert.Equal(Day1.AddSeconds(24), recent[^1].OccurredAt);
        Assert.Equal(25, (await repo.GetSummaryAsync("a")).Total);
    }

    [Fact]
    public async Task DistinctValuesBeyondCap_AreGroupedAsOther()
    {
        var repo = Repo(distinct: 10);

        for (var i = 0; i < 15; i++)
        {
            await repo.AddAsync(new ClickEvent("a", Day1, $"site{i}.example", null));
        }

        var s = await repo.GetSummaryAsync("a");
        Assert.Equal(11, s.ByReferrer.Count);
        Assert.Equal(5, s.ByReferrer[ClickSummary.Other]);
        Assert.Equal(15, s.ByReferrer.Values.Sum());
    }

    [Fact]
    public async Task ConcurrentWrites_AreAllCounted()
    {
        var repo = new InMemoryClickRepository();

        await Task.WhenAll(Enumerable.Range(0, 1_000).Select(i =>
            Task.Run(async () => await repo.AddAsync(new ClickEvent("a", Day1, $"r{i % 7}.example", null)))));

        var s = await repo.GetSummaryAsync("a");
        Assert.Equal(1_000, s.Total);
        Assert.Equal(1_000, s.ByReferrer.Values.Sum());
    }
}
