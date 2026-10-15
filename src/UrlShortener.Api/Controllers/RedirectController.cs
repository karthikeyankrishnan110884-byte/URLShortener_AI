using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>Public redirect endpoint.</summary>
[ApiController]
public sealed class RedirectController(IUrlShortenerService service) : ControllerBase
{
    /// <summary>Redirect to the original URL (302) and record the click.</summary>
    /// <remarks>302 (not 301) so browsers don't cache the redirect: clicks stay countable and deactivation takes effect.
    /// Swagger UI follows redirects automatically, so test with curl or a browser tab.</remarks>
    /// <response code="302">Redirect to original URL.</response>
    /// <response code="404">Unknown code.</response>
    /// <response code="410">Link expired or deactivated.</response>
    [HttpGet("/{code:regex(^[[A-Za-z0-9_-]]{{3,32}}$)}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public IActionResult Follow(string code)
    {
        var target = service.ResolveAndTrack(
            code,
            Request.Headers[HeaderNames.Referer].ToString(),
            Request.Headers[HeaderNames.UserAgent].ToString());

        Response.Headers.CacheControl = "no-store";
        return Redirect(target.AbsoluteUri);
    }
}
