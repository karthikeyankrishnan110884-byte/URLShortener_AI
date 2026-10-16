using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Tests.Integration;

/// <summary>
/// Boots the real API in-memory with production defaults (async analytics, cache on).
/// Each factory instance has its own store, cache, queue and rate-limit state.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string BaseUrl = "https://sho.rt";
    public const string BlockedDomain = "malware.test";

    protected virtual int CreatePermitLimit => 10_000;

    protected virtual int RedirectPermitLimit => 0;

    protected virtual string AnalyticsMode => "Async";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Structural switches are read at composition time, so they go through configuration.
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("Analytics:Mode", AnalyticsMode);
        builder.UseSetting("Safety:BlockedDomains:0", BlockedDomain);
        builder.UseSetting("AllowedHosts", "*");

        builder.ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        builder.ConfigureTestServices(services =>
        {
            services.Configure<ShortenerOptions>(o => o.BaseUrl = BaseUrl);
            services.Configure<RateLimitingOptions>(o =>
            {
                o.CreatePermitLimit = CreatePermitLimit;
                o.RedirectPermitLimit = RedirectPermitLimit;
            });
        });
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

public sealed class TightRateLimitApiFactory : ApiFactory
{
    protected override int CreatePermitLimit => 3;

    protected override int RedirectPermitLimit => 5;
}

/// <summary>Synchronous click recording: used to show the API contract is identical in both modes.</summary>
public sealed class InlineAnalyticsApiFactory : ApiFactory
{
    protected override string AnalyticsMode => "Inline";
}
