using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Composition root for the shortener. All dependencies are registered against interfaces.</summary>
    public static IServiceCollection AddUrlShortener(this IServiceCollection services, IConfiguration configuration)
    {
        AddValidatedOptions<ShortenerOptions>(services, configuration, ShortenerOptions.SectionName);
        AddValidatedOptions<RateLimitingOptions>(services, configuration, RateLimitingOptions.SectionName);
        AddValidatedOptions<AnalyticsOptions>(services, configuration, AnalyticsOptions.SectionName);
        AddValidatedOptions<CacheOptions>(services, configuration, CacheOptions.SectionName);
        AddValidatedOptions<SafetyOptions>(services, configuration, SafetyOptions.SectionName);
        AddValidatedOptions<StorageOptions>(services, configuration, StorageOptions.SectionName);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddSingleton<IUrlValidator, UrlValidator>();
        services.AddSingleton<IDestinationScreen, DomainDenylistScreen>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        AddStorage(services, configuration);
        AddAnalytics(services, configuration);

        services.AddScoped<IUrlShortenerService, UrlShortenerService>();
        services.AddScoped<ILinkAnalyticsService, LinkAnalyticsService>();

        return services;
    }

    /// <summary>
    /// Storage pipeline, outermost first: Cached → (LatencyInjecting, dev only) → InMemory.
    /// Decorators are composed here so services never know about caching (ADR-005).
    /// </summary>
    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        var cache = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var latency = TimeSpan.FromMilliseconds(storage.SimulatedLatencyMs);

        services.AddSingleton<InMemoryLinkRepository>();
        services.AddSingleton<InMemoryClickRepository>();
        services.AddMemoryCache(o => o.SizeLimit = cache.SizeLimit);

        services.AddSingleton<ILinkRepository>(sp =>
        {
            ILinkRepository repo = sp.GetRequiredService<InMemoryLinkRepository>();
            if (latency > TimeSpan.Zero)
            {
                repo = new LatencyInjectingLinkRepository(repo, latency);
            }

            return cache.Enabled
                ? new CachedLinkRepository(repo, sp.GetRequiredService<IMemoryCache>(), sp.GetRequiredService<IOptions<CacheOptions>>(), sp.GetRequiredService<TimeProvider>())
                : repo;
        });

        services.AddSingleton<IClickRepository>(sp =>
        {
            IClickRepository repo = sp.GetRequiredService<InMemoryClickRepository>();
            return latency > TimeSpan.Zero ? new LatencyInjectingClickRepository(repo, latency) : repo;
        });
    }

    /// <summary>Async (Channel + BackgroundService) by default; Inline is kept for comparison and deterministic tests.</summary>
    private static void AddAnalytics(IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration.GetSection(AnalyticsOptions.SectionName).Get<AnalyticsOptions>()?.Mode ?? AnalyticsMode.Async;

        services.AddSingleton<IClickIngestor, ClickIngestor>();

        var health = services.AddHealthChecks();
        if (mode == AnalyticsMode.Inline)
        {
            services.AddSingleton<IClickRecorder, InlineClickRecorder>();
            return;
        }

        services.AddSingleton<ChannelClickQueue>();
        services.AddSingleton<IClickRecorder>(sp => sp.GetRequiredService<ChannelClickQueue>());
        services.AddSingleton<ClickEventProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<ClickEventProcessor>());
        health.AddCheck<ClickQueueHealthCheck>("click-queue", tags: ["ready"]);
    }

    /// <summary>
    /// Per-client-IP limits: a fixed window on link creation (abuse) and a generous sliding window on redirects
    /// (slows code enumeration). Limits are read from IOptions at request time so they can be tuned per environment.
    /// Rejections are RFC 7807 with Retry-After.
    /// </summary>
    public static IServiceCollection AddShortenerRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Results.Problem(
                        statusCode: StatusCodes.Status429TooManyRequests,
                        title: "Too many requests",
                        detail: "Rate limit exceeded. Retry after the period in the Retry-After header.")
                    .ExecuteAsync(context.HttpContext);
            };

            o.AddPolicy(RateLimitingOptions.CreatePolicy, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.CreatePermitLimit,
                        Window = TimeSpan.FromSeconds(limits.CreateWindowSeconds),
                        QueueLimit = 0,
                    });
            });

            o.AddPolicy(RateLimitingOptions.RedirectPolicy, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                if (limits.RedirectPermitLimit == 0)
                {
                    return RateLimitPartition.GetNoLimiter("redirect-unlimited");
                }

                return RateLimitPartition.GetSlidingWindowLimiter(
                    ClientKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.RedirectPermitLimit,
                        Window = TimeSpan.FromSeconds(limits.RedirectWindowSeconds),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    /// <summary>Honour X-Forwarded-For/Proto only from configured proxies (otherwise any client could spoof its IP).</summary>
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var proxy in settings.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                {
                    o.KnownProxies.Add(ip);
                }
            }
        });

        return services;
    }

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static void AddValidatedOptions<T>(IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
