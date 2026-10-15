using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Composition root for the shortener. All dependencies are registered against interfaces.</summary>
    public static IServiceCollection AddUrlShortener(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ShortenerOptions>()
            .Bind(configuration.GetSection(ShortenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singletons: the store is in-memory and the rest are stateless/thread-safe.
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILinkRepository, InMemoryLinkRepository>();
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddSingleton<IUrlValidator, UrlValidator>();
        services.AddScoped<IUrlShortenerService, UrlShortenerService>();

        return services;
    }

    /// <summary>Fixed-window, per-client-IP limit on link creation (abuse protection). Limits come from IOptions so they can be tuned per environment.</summary>
    public static IServiceCollection AddCreateLinkRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RateLimitingOptions.CreatePolicy, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.CreatePermitLimit,
                        Window = TimeSpan.FromSeconds(limits.CreateWindowSeconds),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
