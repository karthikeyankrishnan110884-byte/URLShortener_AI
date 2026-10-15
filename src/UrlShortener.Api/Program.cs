using System.Reflection;
using Microsoft.OpenApi.Models;
using UrlShortener.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o => o.Filters.Add<DomainExceptionFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddUrlShortener(builder.Configuration);
builder.Services.AddCreateLinkRateLimiting();
builder.Services.AddHealthChecks();

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

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Swagger is enabled in all environments for the prototype; restrict to Development in production.
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "URL Shortener API v1"));

app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
