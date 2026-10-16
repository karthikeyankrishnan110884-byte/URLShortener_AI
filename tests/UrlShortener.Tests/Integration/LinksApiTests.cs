using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;

namespace UrlShortener.Tests.Integration;

public class LinksApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateNonRedirectingClient();

    private async Task<LinkResponse> CreateLinkAsync(string url, string? alias = null, DateTimeOffset? expiresAt = null)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url, customAlias = alias, expiresAt });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LinkResponse>())!;
    }

    [Fact]
    public async Task Post_ValidUrl_Returns201WithShortUrlAndLocationHeader()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url = "https://example.com/article" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(body);
        Assert.Equal($"{ApiFactory.BaseUrl}/{body.Code}", body.ShortUrl);
        Assert.Equal("https://example.com/article", body.OriginalUrl);
        Assert.True(body.IsActive);
        Assert.EndsWith($"/api/v1/links/{body.Code}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task EndToEnd_CreateRedirectThenStats_ReflectsTheClick()
    {
        var link = await CreateLinkAsync("https://example.com/e2e");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{link.Code}");
        request.Headers.Referrer = new Uri("https://news.ycombinator.com/item?id=1");
        var redirect = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("https://example.com/e2e", redirect.Headers.Location!.ToString());
        Assert.True(redirect.Headers.CacheControl!.NoStore);

        var stats = await Eventually.GetAsync(
            () => _client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats")!,
            s => s!.TotalClicks == 1);
        Assert.Equal(1, stats!.TotalClicks);
        Assert.Equal("news.ycombinator.com", Assert.Single(stats.TopReferrers).Referrer);
    }

    [Fact]
    public async Task Get_ExistingLink_ReturnsMetadataIncludingClickCount()
    {
        var link = await CreateLinkAsync("https://example.com/meta");
        await _client.GetAsync($"/{link.Code}");
        await _client.GetAsync($"/{link.Code}");

        var fetched = await Eventually.GetAsync(
            () => _client.GetFromJsonAsync<LinkResponse>($"/api/v1/links/{link.Code}")!,
            l => l!.ClickCount == 2);

        Assert.Equal(2, fetched!.ClickCount);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("http://127.0.0.1/admin")]
    public async Task Post_InvalidUrl_Returns400ProblemDetails(string url)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(400, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Post_MissingBody_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_DuplicateAlias_Returns409()
    {
        await CreateLinkAsync("https://a.example", alias: "launch-day");

        var response = await _client.PostAsJsonAsync("/api/v1/links", new { url = "https://b.example", customAlias = "launch-day" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Redirect_UnknownCode_Returns404()
    {
        var response = await _client.GetAsync("/zzzzzzz");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ThenRedirect_Returns410Gone_ButMetadataRemainsReadable()
    {
        var link = await CreateLinkAsync("https://example.com/retired");

        var delete = await _client.DeleteAsync($"/api/v1/links/{link.Code}");
        var redirect = await _client.GetAsync($"/{link.Code}");
        var metadata = await _client.GetFromJsonAsync<LinkResponse>($"/api/v1/links/{link.Code}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, redirect.StatusCode);
        Assert.False(metadata!.IsActive);
    }

    [Fact]
    public async Task Delete_UnknownCode_Returns404()
    {
        var response = await _client.DeleteAsync("/api/v1/links/missing123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExpiryInPast_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/links",
            new { url = "https://example.com", expiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Health_And_SwaggerDocument_AreServed()
    {
        var health = await _client.GetAsync("/health");
        var healthz = await _client.GetAsync("/healthz");
        var readyz = await _client.GetAsync("/readyz");
        var swagger = await _client.GetStringAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthz.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readyz.StatusCode);
        Assert.Contains("/stats/daily", swagger);
        Assert.Contains("/api/v1/links", swagger);
        Assert.Contains("/{code}", swagger);
    }
}
