using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Api.Controllers;

/// <summary>Public redirect endpoint.</summary>
[ApiController]
[EnableRateLimiting(RateLimitingOptions.RedirectPolicy)]
public sealed class RedirectController(IUrlShortenerService service) : ControllerBase
{
    private const string CodeRoute = "/{code:regex(^[[A-Za-z0-9_-]]{{3,32}}$)}";

    /// <summary>Redirect to the original URL (302) and record the click.</summary>
    /// <remarks>
    /// 302 (not 301) so browsers don't cache the redirect: clicks stay countable and deactivation takes effect.
    /// Optional <c>utm_source</c>, <c>utm_medium</c>, <c>utm_campaign</c> query parameters are recorded for campaign
    /// analytics (they are not forwarded to the destination). Swagger UI follows redirects, so test with curl or a browser tab.
    /// </remarks>
    /// <param name="code">Short code.</param>
    /// <param name="utmSource">Campaign source, e.g. newsletter.</param>
    /// <param name="utmMedium">Campaign medium, e.g. email.</param>
    /// <param name="utmCampaign">Campaign name, e.g. spring-sale.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="302">Redirect to original URL.</response>
    /// <response code="404">Unknown code.</response>
    /// <response code="410">Link expired, deactivated or blocked.</response>
    /// <response code="429">Too many requests from this client.</response>
    [HttpGet(CodeRoute)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Follow(
        string code,
        [FromQuery(Name = "utm_source")] string? utmSource,
        [FromQuery(Name = "utm_medium")] string? utmMedium,
        [FromQuery(Name = "utm_campaign")] string? utmCampaign,
        CancellationToken cancellationToken)
    {
        var context = new ClickContext(
            Request.Headers[HeaderNames.Referer].ToString(),
            Request.Headers[HeaderNames.UserAgent].ToString(),
            utmSource,
            utmMedium,
            utmCampaign);

        var target = await service.ResolveAsync(code, context, cancellationToken);
        return NoStoreRedirect(target);
    }

    /// <summary>Same as GET but does not count a click (link checkers and unfurlers often send HEAD).</summary>
    /// <param name="code">Short code.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="302">Redirect to original URL.</response>
    /// <response code="404">Unknown code.</response>
    /// <response code="410">Link expired, deactivated or blocked.</response>
    [HttpHead(CodeRoute)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Peek(string code, CancellationToken cancellationToken) =>
        NoStoreRedirect(await service.PeekAsync(code, cancellationToken));

    private RedirectResult NoStoreRedirect(Uri target)
    {
        Response.Headers.CacheControl = "no-store";
        return Redirect(target.AbsoluteUri);
    }
}
