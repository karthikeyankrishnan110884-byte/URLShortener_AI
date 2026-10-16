using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using UrlShortener.Api.Contracts;

namespace UrlShortener.Tests.Integration;

public class RateLimitTests(TightRateLimitApiFactory factory) : IClassFixture<TightRateLimitApiFactory>
{
    [Fact]
    public async Task Post_BeyondPermitLimit_Returns429ProblemWithRetryAfter_WhileRedirectsHaveTheirOwnBudget()
    {
        var client = factory.CreateNonRedirectingClient();
        var statuses = new List<HttpStatusCode>();
        string? code = null;
        HttpResponseMessage? rejected = null;

        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/links", new { url = $"https://example.com/{i}" });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                code ??= (await response.Content.ReadFromJsonAsync<LinkResponse>())!.Code;
            }
            else
            {
                rejected = response;
            }
        }

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, statuses);
        Assert.Equal("application/problem+json", rejected!.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(429, (await rejected.Content.ReadFromJsonAsync<ProblemDetails>())!.Status);

        // Create budget exhausted, redirect budget (5) is separate.
        var redirects = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            redirects.Add((await client.GetAsync($"/{code}")).StatusCode);
        }

        Assert.Equal(5, redirects.Count(s => s == HttpStatusCode.Found));
        Assert.Equal(HttpStatusCode.TooManyRequests, redirects[^1]);
    }
}
