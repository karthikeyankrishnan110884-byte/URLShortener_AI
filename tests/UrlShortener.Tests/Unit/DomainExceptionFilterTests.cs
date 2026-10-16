using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Tests.Unit;

public class DomainExceptionFilterTests
{
    public static TheoryData<DomainException, int> Cases => new()
    {
        { new InvalidUrlException("x"), 400 },
        { new InvalidAliasException("x"), 400 },
        { new InvalidExpiryException("x"), 400 },
        { new InvalidQueryException("x"), 400 },
        { new UnsafeDestinationException("x"), 400 },
        { new AliasConflictException("x"), 409 },
        { new IdempotencyKeyInProgressException(), 409 },
        { new IdempotencyKeyMismatchException(), 422 },
        { new LinkNotFoundException("x"), 404 },
        { new LinkGoneException("x", "expired"), 410 },
        { new CodeGenerationException(3), 503 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryDomainException_MapsToItsStatusCode(DomainException exception, int expected)
    {
        Assert.Equal(expected, DomainExceptionFilter.MapStatus(exception).Status);
    }
}
