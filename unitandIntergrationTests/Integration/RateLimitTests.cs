using System.Net;
using System.Net.Http.Json;

namespace UrlShortener.Tests.Integration;

public class RateLimitTests(TightRateLimitApiFactory factory) : IClassFixture<TightRateLimitApiFactory>
{
    [Fact]
    public async Task Post_BeyondPermitLimit_Returns429_WhileRedirectsAreNotLimited()
    {
        var client = factory.CreateNonRedirectingClient();
        var statuses = new List<HttpStatusCode>();
        string? code = null;

        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/links", new { url = $"https://example.com/{i}" });
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                code ??= (await response.Content.ReadFromJsonAsync<UrlShortener.Api.Contracts.LinkResponse>())!.Code;
            }
        }

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, statuses);

        var redirect = await client.GetAsync($"/{code}");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
    }
}
