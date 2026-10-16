using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Api.Services.Analytics;

public sealed class LinkAnalyticsService(
    ILinkRepository links,
    IClickRepository clicks,
    TimeProvider timeProvider,
    IOptions<AnalyticsOptions> options) : ILinkAnalyticsService
{
    internal const int DefaultDailyRangeDays = 30;

    public async Task<LinkStatsResponse> GetStatsAsync(string code, int top = 5, CancellationToken cancellationToken = default)
    {
        var link = await links.GetByCodeAsync(code, cancellationToken) ?? throw new LinkNotFoundException(code);
        var summary = await clicks.GetSummaryAsync(code, cancellationToken);

        return new LinkStatsResponse(
            link.Code,
            summary.Total,
            link.LastAccessedAt,
            summary.ByDay.OrderBy(d => d.Key).Select(d => new DailyClicks(d.Key, d.Value)).ToList(),
            Top(summary.ByReferrer, top).Select(v => new ReferrerClicks(v.Value, v.Clicks)).ToList(),
            Top(summary.BySource, top),
            Top(summary.ByMedium, top),
            Top(summary.ByCampaign, top),
            Top(summary.ByDevice, int.MaxValue));
    }

    public async Task<DailyStatsResponse> GetDailyAsync(string code, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var link = await links.GetByCodeAsync(code, cancellationToken) ?? throw new LinkNotFoundException(code);

        var end = to ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var start = from ?? end.AddDays(-(DefaultDailyRangeDays - 1));

        if (start > end)
        {
            throw new InvalidQueryException("'from' must be on or before 'to'.");
        }

        var days = end.DayNumber - start.DayNumber + 1;
        if (days > options.Value.MaxDailyRangeDays)
        {
            throw new InvalidQueryException($"The range may cover at most {options.Value.MaxDailyRangeDays} days.");
        }

        var byDay = (await clicks.GetSummaryAsync(code, cancellationToken)).ByDay;
        var series = Enumerable.Range(0, days)
            .Select(i => start.AddDays(i))
            .Select(d => new DailyClicks(d, byDay.GetValueOrDefault(d)))
            .ToList();

        return new DailyStatsResponse(link.Code, start, end, series.Sum(d => d.Clicks), series);
    }

    /// <summary>Largest first; ties broken alphabetically so output is stable. "(none)" (untagged) is excluded from UTM lists.</summary>
    private static List<ValueClicks> Top(IReadOnlyDictionary<string, long> counts, int take) =>
        counts
            .Where(kv => kv.Key != ClickSummary.None)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(take)
            .Select(kv => new ValueClicks(kv.Key, kv.Value))
            .ToList();
}
