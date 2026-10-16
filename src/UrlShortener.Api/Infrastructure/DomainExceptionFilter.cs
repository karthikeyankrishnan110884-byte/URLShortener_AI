using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Infrastructure;

/// <summary>
/// Maps expected domain failures to RFC 7807 problem details.
/// Runs inside MVC so expected 4xx outcomes are not logged as unhandled errors;
/// anything else falls through to the global exception handler (generic 500, no internals leaked).
/// </summary>
public sealed class DomainExceptionFilter(ProblemDetailsFactory problemDetailsFactory) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainException exception)
        {
            return;
        }

        var (status, title) = MapStatus(exception);
        var problem = problemDetailsFactory.CreateProblemDetails(
            context.HttpContext, status, title, detail: exception.Message, instance: context.HttpContext.Request.Path);

        context.Result = new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
        context.ExceptionHandled = true;
    }

    internal static (int Status, string Title) MapStatus(DomainException exception) => exception switch
    {
        InvalidUrlException or InvalidAliasException or InvalidExpiryException or InvalidQueryException
            => (StatusCodes.Status400BadRequest, "Invalid request"),
        UnsafeDestinationException => (StatusCodes.Status400BadRequest, "Destination not allowed"),
        AliasConflictException => (StatusCodes.Status409Conflict, "Alias already in use"),
        IdempotencyKeyInProgressException => (StatusCodes.Status409Conflict, "Request in progress"),
        IdempotencyKeyMismatchException => (StatusCodes.Status422UnprocessableEntity, "Idempotency-Key reused"),
        LinkNotFoundException => (StatusCodes.Status404NotFound, "Link not found"),
        LinkGoneException => (StatusCodes.Status410Gone, "Link no longer available"),
        CodeGenerationException => (StatusCodes.Status503ServiceUnavailable, "Could not allocate a short code"),
        _ => (StatusCodes.Status400BadRequest, "Request failed"),
    };
}
