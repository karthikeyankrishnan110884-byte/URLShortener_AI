using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;

namespace UrlShortener.Tests.Integration;

/// <summary>Scenario B/C behaviour over the real HTTP pipeline: idempotency, safety, campaign analytics, observability.</summary>
public class HardeningApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateNonRedirectingClient();

    private async Task<LinkResponse> CreateAsync(string url, string? alias = null)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url, customAlias = alias });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LinkResponse>())!;
    }

    private Task<HttpResponseMessage> PostWithKeyAsync(string key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/links") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task IdempotencyKey_Retry_ReturnsSameLink_AndDifferentBodyIs422()
    {
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(key, new { url = "https://example.com/idem" });
        var retry = await PostWithKeyAsync(key, new { url = "https://example.com/idem" });
        var misuse = await PostWithKeyAsync(key, new { url = "https://example.com/other" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<LinkResponse>())!.Code,
            (await retry.Content.ReadFromJsonAsync<LinkResponse>())!.Code);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, misuse.StatusCode);
    }

    [Fact]
    public async Task IdempotencyKey_TooLong_Is400()
    {
        var response = await PostWithKeyAsync(new string('k', 65), new { url = "https://example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ToDenylistedDomain_Is400ProblemDetails()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url = $"https://login.{ApiFactory.BlockedDomain}/" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Destination not allowed", problem!.Title);
    }

    [Fact]
    public async Task Create_ExpiryBeyondMaximum_Is400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links",
            new { url = "https://example.com", expiresAt = DateTimeOffset.UtcNow.AddYears(20) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_OversizedBody_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url = "https://example.com/" + new string('a', 20_000) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_WithUtmTags_FeedsCampaignAndDeviceAnalytics_AndDailySeries()
    {
        var link = await CreateAsync("https://example.com/campaign");

        foreach (var (source, ua) in new[] { ("newsletter", "Mozilla/5.0 (iPhone) Mobile"), ("newsletter", "Mozilla/5.0 (Windows NT 10.0)"), ("twitter", "Mozilla/5.0 (Windows NT 10.0)") })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}?utm_source={source}&utm_medium=email&utm_campaign=Spring-Sale");
            request.Headers.UserAgent.ParseAdd(ua);
            var redirect = await _client.SendAsync(request);
            Assert.Equal("https://example.com/campaign", redirect.Headers.Location!.ToString()); // tags are not forwarded
        }

        var stats = await Eventually.GetAsync(
            () => _client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats")!,
            s => s!.TotalClicks == 3);

        Assert.Equal(new ValueClicks("newsletter", 2), stats!.TopSources![0]);
        Assert.Equal(new ValueClicks("spring-sale", 3), Assert.Single(stats.TopCampaigns!));
        Assert.Equal(new ValueClicks("email", 3), Assert.Single(stats.TopMediums!));
        Assert.Contains(new ValueClicks("desktop", 2), stats.Devices!);
        Assert.Contains(new ValueClicks("mobile", 1), stats.Devices!);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daily = await _client.GetFromJsonAsync<DailyStatsResponse>(
            $"/api/v1/links/{link.Code}/stats/daily?from={today.AddDays(-6):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(7, daily!.Days.Count);
        Assert.Equal(3, daily.TotalClicks);
        Assert.Equal(3, daily.Days[^1].Clicks);
    }

    [Fact]
    public async Task DailyStats_InvalidRange_Is400()
    {
        var link = await CreateAsync("https://example.com/range");

        var response = await _client.GetAsync($"/api/v1/links/{link.Code}/stats/daily?from=2026-10-10&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Head_Redirects_WithoutCountingAClick()
    {
        var link = await CreateAsync("https://example.com/head");

        var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/{link.Code}"));
        await _client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Found, head.StatusCode);
        var stats = await Eventually.GetAsync(
            () => _client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats")!,
            s => s!.TotalClicks >= 1);
        await Task.Delay(100);
        stats = await _client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats");
        Assert.Equal(1, stats!.TotalClicks);
    }

    [Fact]
    public async Task Deactivate_IsVisibleImmediately_DespiteCache()
    {
        var link = await CreateAsync("https://example.com/cached");
        Assert.Equal(HttpStatusCode.Found, (await _client.GetAsync($"/{link.Code}")).StatusCode); // warms cache

        await _client.DeleteAsync($"/api/v1/links/{link.Code}");

        Assert.Equal(HttpStatusCode.Gone, (await _client.GetAsync($"/{link.Code}")).StatusCode);
    }

    [Fact]
    public async Task UnknownCode_ThenCreatedWithThatAlias_IsServed_DespiteNegativeCache()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/late-alias")).StatusCode);

        await CreateAsync("https://example.com/late", alias: "late-alias");

        Assert.Equal(HttpStatusCode.Found, (await _client.GetAsync("/late-alias")).StatusCode);
    }

    [Fact]
    public async Task RequestId_IsEchoed_AndIncludedInProblemDetails()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/links/doesnotexist");
        request.Headers.Add("X-Request-ID", "client-abc-123");

        var response = await _client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal("client-abc-123", response.Headers.GetValues("X-Request-ID").Single());
        Assert.Equal("client-abc-123", problem!.Extensions["requestId"]!.ToString());
        Assert.NotNull(problem.Extensions["traceId"]); // W3C trace id for distributed tracing
    }

    [Fact]
    public async Task UnsafeRequestId_IsReplacedByServerId()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.TryAddWithoutValidation("X-Request-ID", "bad id with spaces <script>");

        var response = await _client.SendAsync(request);

        Assert.DoesNotContain("<script>", response.Headers.GetValues("X-Request-ID").Single());
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await _client.GetAsync("/healthz");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Readyz_ReportsClickQueueDetails()
    {
        var response = await _client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
