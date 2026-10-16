using System.Text.RegularExpressions;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Request correlation and baseline security headers.
/// <list type="bullet">
/// <item>Accepts a caller's <c>X-Request-ID</c> if it is short and safe, otherwise uses the server trace id.</item>
/// <item>Echoes it on the response, sets it as <see cref="HttpContext.TraceIdentifier"/> (so it appears as <c>traceId</c>
/// in problem details) and opens a logging scope so every log line for the request carries it.</item>
/// </list>
/// </summary>
public sealed partial class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public const string HeaderName = "X-Request-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var requestId = SafeId().IsMatch(incoming) ? incoming : context.TraceIdentifier;
        context.TraceIdentifier = requestId;

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderName] = requestId;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            return Task.CompletedTask;
        });

        using (logger.BeginScope("CorrelationId:{CorrelationId}", requestId))
        {
            await next(context);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex SafeId();
}
