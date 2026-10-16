using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Api.Controllers;

/// <summary>Manage short links and read their analytics.</summary>
[ApiController]
[Route("api/v1/links")]
[Produces("application/json")]
public sealed class LinksController(
    IUrlShortenerService service,
    ILinkAnalyticsService analytics,
    IOptions<ShortenerOptions> options) : ControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    private readonly string _baseUrl = options.Value.BaseUrl;

    /// <summary>Create a short link.</summary>
    /// <remarks>
    /// Send an <c>Idempotency-Key</c> header (1-64 chars) to make retries safe: repeating the same request with the
    /// same key returns the original link (header <c>Idempotent-Replayed: true</c>) instead of creating another one.
    /// </remarks>
    /// <param name="request">Target URL, optional alias and expiry.</param>
    /// <param name="idempotencyKey">Optional client-generated key, e.g. a UUID.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="201">Link created (or replayed for a repeated Idempotency-Key).</response>
    /// <response code="400">URL, alias or expiry is invalid, or the destination is blocked.</response>
    /// <response code="409">Custom alias already taken, or the same Idempotency-Key is still in progress.</response>
    /// <response code="422">Idempotency-Key reused with a different body.</response>
    /// <response code="429">Too many create requests.</response>
    [HttpPost]
    [EnableRateLimiting(RateLimitingOptions.CreatePolicy)]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LinkResponse>> Create(
        [FromBody] CreateLinkRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is { Length: > 64 })
        {
            return ValidationProblem(detail: $"{IdempotencyHeader} must be at most 64 characters.");
        }

        var result = await service.CreateAsync(request, idempotencyKey, cancellationToken);
        if (result.Replayed)
        {
            Response.Headers[ReplayedHeader] = "true";
        }

        return CreatedAtAction(nameof(Get), new { code = result.Link.Code }, result.Link.ToResponse(_baseUrl));
    }

    /// <summary>Get link metadata.</summary>
    /// <response code="200">Link metadata.</response>
    /// <response code="404">Unknown code.</response>
    [HttpGet("{code}")]
    [ProducesResponseType<LinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LinkResponse>> Get(string code, CancellationToken cancellationToken) =>
        (await service.GetAsync(code, cancellationToken)).ToResponse(_baseUrl);

    /// <summary>Get click analytics: totals, clicks per day, top referrers, UTM sources/mediums/campaigns, devices.</summary>
    /// <param name="code">Short code.</param>
    /// <param name="top">How many entries to return in each "top" list (1-50).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">Aggregated analytics.</response>
    /// <response code="404">Unknown code.</response>
    [HttpGet("{code}/stats")]
    [ProducesResponseType<LinkStatsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LinkStatsResponse>> GetStats(string code, [FromQuery] int top = 5, CancellationToken cancellationToken = default) =>
        await analytics.GetStatsAsync(code, Math.Clamp(top, 1, 50), cancellationToken);

    /// <summary>Zero-filled daily click series for charting. Defaults to the last 30 UTC days.</summary>
    /// <param name="code">Short code.</param>
    /// <param name="from">First UTC day (inclusive), e.g. 2026-10-01.</param>
    /// <param name="to">Last UTC day (inclusive). Defaults to today.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="200">One entry per day in the range.</response>
    /// <response code="400">Invalid or too large range.</response>
    /// <response code="404">Unknown code.</response>
    [HttpGet("{code}/stats/daily")]
    [ProducesResponseType<DailyStatsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DailyStatsResponse>> GetDaily(
        string code, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        await analytics.GetDailyAsync(code, from, to, cancellationToken);

    /// <summary>Deactivate a link (soft delete). Subsequent redirects return 410 Gone. Idempotent.</summary>
    /// <response code="204">Deactivated (or already inactive).</response>
    /// <response code="404">Unknown code.</response>
    [HttpDelete("{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(string code, CancellationToken cancellationToken)
    {
        await service.DeactivateAsync(code, cancellationToken);
        return NoContent();
    }
}
