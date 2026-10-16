using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services.Analytics;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public class LinkAnalyticsServiceTests
{
    private readonly ServiceHarness _h = new();

    private async Task<string> CreateAsync() =>
        (await _h.CreateService().CreateAsync(new CreateLinkRequest { Url = "https://example.com" })).Link.Code;

    [Fact]
    public async Task GetStats_AggregatesClicksByDayReferrerCampaignAndDevice()
    {
        var sut = _h.CreateService();
        var code = await CreateAsync();

        await sut.ResolveAsync(code, new ClickContext("https://google.com/search", "Mozilla/5.0 (Windows NT 10.0)", "google", "cpc", "spring"));
        await sut.ResolveAsync(code, new ClickContext("https://google.com/other", "Mozilla/5.0 (iPhone) Mobile", "google", "cpc", "spring"));
        await sut.ResolveAsync(code, new ClickContext(null, "Googlebot/2.1"));
        _h.Clock.Advance(TimeSpan.FromDays(1));
        await sut.ResolveAsync(code, new ClickContext("https://twitter.com/x", null, "twitter", null, "spring"));

        var stats = await _h.CreateAnalytics().GetStatsAsync(code);

        Assert.Equal(4, stats.TotalClicks);
        Assert.Equal(ServiceHarness.Start.AddDays(1), stats.LastAccessedAt);
        Assert.Equal(
            new[] { new DailyClicks(new DateOnly(2026, 10, 10), 3), new DailyClicks(new DateOnly(2026, 10, 11), 1) },
            stats.ClicksByDay);
        Assert.Equal(new ReferrerClicks("google.com", 2), stats.TopReferrers[0]);
        Assert.Contains(new ReferrerClicks(ClickSummary.Direct, 1), stats.TopReferrers);
        Assert.Equal(new ValueClicks("google", 2), stats.TopSources![0]);
        Assert.Equal(new ValueClicks("spring", 3), Assert.Single(stats.TopCampaigns!));
        Assert.DoesNotContain(stats.TopSources, v => v.Value == ClickSummary.None);
        Assert.Contains(new ValueClicks(UserAgentClassifier.Bot, 1), stats.Devices!);
        Assert.Contains(new ValueClicks(UserAgentClassifier.Desktop, 1), stats.Devices!);
    }

    [Fact]
    public async Task GetStats_TopN_IsHonouredAndStable()
    {
        var sut = _h.CreateService();
        var code = await CreateAsync();
        foreach (var r in new[] { "b.example", "a.example", "c.example", "a.example" })
        {
            await sut.ResolveAsync(code, new ClickContext($"https://{r}/", null));
        }

        var stats = await _h.CreateAnalytics().GetStatsAsync(code, top: 2);

        Assert.Equal(new[] { new ReferrerClicks("a.example", 2), new ReferrerClicks("b.example", 1) }, stats.TopReferrers);
    }

    [Fact]
    public async Task GetStats_ForNewLink_IsEmptyNotNull()
    {
        var code = await CreateAsync();

        var stats = await _h.CreateAnalytics().GetStatsAsync(code);

        Assert.Equal(0, stats.TotalClicks);
        Assert.Null(stats.LastAccessedAt);
        Assert.Empty(stats.ClicksByDay);
        Assert.Empty(stats.TopReferrers);
        Assert.Empty(stats.TopCampaigns!);
    }

    [Fact]
    public async Task GetStats_UnknownCode_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<LinkNotFoundException>(() => _h.CreateAnalytics().GetStatsAsync("missing"));
    }

    [Fact]
    public async Task GetDaily_ReturnsZeroFilledSeries_ForRequestedRange()
    {
        var sut = _h.CreateService();
        var code = await CreateAsync();
        await sut.ResolveAsync(code, ClickContext.Empty);
        _h.Clock.Advance(TimeSpan.FromDays(2));
        await sut.ResolveAsync(code, ClickContext.Empty);
        await sut.ResolveAsync(code, ClickContext.Empty);

        var daily = await _h.CreateAnalytics().GetDailyAsync(code, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 13));

        Assert.Equal(5, daily.Days.Count);
        Assert.Equal(new long[] { 0, 1, 0, 2, 0 }, daily.Days.Select(d => d.Clicks));
        Assert.Equal(3, daily.TotalClicks);
    }

    [Fact]
    public async Task GetDaily_DefaultsToLast30DaysEndingToday()
    {
        var code = await CreateAsync();

        var daily = await _h.CreateAnalytics().GetDailyAsync(code, null, null);

        Assert.Equal(new DateOnly(2026, 10, 10), daily.To);
        Assert.Equal(new DateOnly(2026, 9, 11), daily.From);
        Assert.Equal(30, daily.Days.Count);
    }

    [Fact]
    public async Task GetDaily_RejectsInvertedOrOversizedRange()
    {
        var code = await CreateAsync();
        var analytics = _h.CreateAnalytics(maxDailyRangeDays: 31);

        await Assert.ThrowsAsync<InvalidQueryException>(() => analytics.GetDailyAsync(code, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1)));
        await Assert.ThrowsAsync<InvalidQueryException>(() => analytics.GetDailyAsync(code, new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1)));
    }
}
