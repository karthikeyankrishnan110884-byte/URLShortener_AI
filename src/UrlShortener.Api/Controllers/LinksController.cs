using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>Manage short links and read their analytics.</summary>
[ApiController]
[Route("api/v1/links")]
[Produces("application/json")]
public sealed class LinksController(IUrlShortenerService service, IOptions<ShortenerOptions> options) : ControllerBase
{
    private readonly string _baseUrl = options.Value.BaseUrl;

    /// <summary>Create a short link.</summary>
    /// <response code="201">Link created.</response>
    /// <response code="400">URL, alias or expiry is invalid.</response>
    /// <response code="409">Custom alias already taken.</response>
    /// <response code="429">Too many create requests.</response>
    [HttpPost]
    [EnableRateLimiting(RateLimitingOptions.CreatePolicy)]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<LinkResponse> Create([FromBody] CreateLinkRequest request)
    {
        var link = service.Create(request);
        return CreatedAtAction(nameof(Get), new { code = link.Code }, link.ToResponse(_baseUrl));
    }

    /// <summary>Get link metadata.</summary>
    [HttpGet("{code}")]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<LinkResponse> Get(string code) => service.Get(code).ToResponse(_baseUrl);

    /// <summary>Get click analytics: totals, clicks per day, top referrers.</summary>
    [HttpGet("{code}/stats")]
    [ProducesResponseType<LinkStatsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<LinkStatsResponse> GetStats(string code) => service.GetStats(code);

    /// <summary>Deactivate a link (soft delete). Subsequent redirects return 410 Gone.</summary>
    [HttpDelete("{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult Deactivate(string code)
    {
        service.Deactivate(code);
        return NoContent();
    }
}
