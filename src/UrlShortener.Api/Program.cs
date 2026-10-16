using System.Reflection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using UrlShortener.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o => o.Filters.Add<DomainExceptionFilter>());
builder.Services.AddProblemDetails(o =>
    o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["requestId"] = c.HttpContext.TraceIdentifier);
builder.Services.AddUrlShortener(builder.Configuration);
builder.Services.AddShortenerRateLimiting();
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);

var swaggerEnabled = builder.Configuration.GetValue("Swagger:Enabled", builder.Environment.IsDevelopment());
if (swaggerEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "URL Shortener API",
            Version = "v1",
            Description = "Create short links, redirect, and read click analytics.",
        });
        var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xml))
        {
            c.IncludeXmlComments(xml);
        }
    });
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<RequestContextMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Swagger is on in Development by default; elsewhere only when Swagger:Enabled=true.
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "URL Shortener API v1"));
}

app.UseRateLimiter();

// Liveness: the process is up (no dependency checks, so a slow dependency never triggers a restart loop).
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }); // backward-compatible alias

// Readiness: dependencies tagged "ready" (click queue headroom). Degraded still returns 200; Unhealthy returns 503.
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapControllers();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
