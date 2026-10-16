using UrlShortener.Api.Contracts;

namespace UrlShortener.Api.Services.Analytics;

/// <summary>Read side of analytics (split from the link service for single responsibility).</summary>
public interface ILinkAnalyticsService
{
    Task<LinkStatsResponse> GetStatsAsync(string code, int top = 5, CancellationToken cancellationToken = default);

    Task<DailyStatsResponse> GetDailyAsync(string code, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
}
