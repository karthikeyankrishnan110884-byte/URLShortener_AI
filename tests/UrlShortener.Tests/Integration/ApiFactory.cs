using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Tests.Integration;

/// <summary>Boots the real API in-memory. Each factory instance has its own store and rate-limit state.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string BaseUrl = "https://sho.rt";

    protected virtual int CreatePermitLimit => 10_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.Configure<ShortenerOptions>(o => o.BaseUrl = BaseUrl);
            services.Configure<RateLimitingOptions>(o => o.CreatePermitLimit = CreatePermitLimit);
        });
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

public sealed class TightRateLimitApiFactory : ApiFactory
{
    protected override int CreatePermitLimit => 3;
}
