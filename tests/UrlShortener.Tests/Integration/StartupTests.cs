using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;

namespace UrlShortener.Tests.Integration;

public class StartupTests
{
    [Fact]
    public void InvalidConfiguration_StopsTheAppFromStarting()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Shortener:CodeLength", "2");
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.IsType<OptionsValidationException>(ex.GetBaseException());
    }

    [Fact]
    public async Task Production_DoesNotExposeSwagger()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("AllowedHosts", "*");
        });

        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Production_RejectsUnexpectedHostHeader()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "attacker.example";

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InlineAnalyticsMode_HasTheSameContract()
    {
        using var factory = new InlineAnalyticsApiFactory();
        var client = factory.CreateNonRedirectingClient();

        var created = await client.PostAsJsonAsync("/api/v1/links", new { url = "https://example.com/inline" });
        var link = (await created.Content.ReadFromJsonAsync<LinkResponse>())!;
        await client.GetAsync($"/{link.Code}");

        // Inline mode is synchronous: no polling needed.
        var stats = await client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats");
        Assert.Equal(1, stats!.TotalClicks);
        Assert.Equal("Healthy", await client.GetStringAsync("/readyz"));
    }

    [Fact]
    public async Task SimulatedStorageLatency_AndCacheDisabled_StillServeCorrectly()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("AllowedHosts", "*");
            b.UseSetting("Storage:SimulatedLatencyMs", "1");
            b.UseSetting("Cache:Enabled", "false");
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var created = await client.PostAsJsonAsync("/api/v1/links", new { url = "https://example.com/slow" });
        var link = (await created.Content.ReadFromJsonAsync<LinkResponse>())!;
        var redirect = await client.GetAsync($"/{link.Code}");

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        var stats = await Eventually.GetAsync(
            () => client.GetFromJsonAsync<LinkStatsResponse>($"/api/v1/links/{link.Code}/stats")!,
            s => s!.TotalClicks == 1);
        Assert.Equal(1, stats!.TotalClicks);
    }
}
