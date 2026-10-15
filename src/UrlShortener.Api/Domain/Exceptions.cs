namespace UrlShortener.Api.Domain;

/// <summary>Base type for expected business-rule failures; mapped to RFC 7807 responses by the API layer.</summary>
public abstract class DomainException(string message) : Exception(message);

public sealed class InvalidUrlException(string message) : DomainException(message);

public sealed class InvalidAliasException(string message) : DomainException(message);

public sealed class InvalidExpiryException(string message) : DomainException(message);

public sealed class AliasConflictException(string alias) : DomainException($"The alias '{alias}' is already in use.");

public sealed class LinkNotFoundException(string code) : DomainException($"No link exists for code '{code}'.");

public sealed class LinkGoneException(string code, string reason) : DomainException($"Link '{code}' is no longer available: {reason}.");

public sealed class CodeGenerationException(int attempts)
    : DomainException($"Could not generate a unique short code after {attempts} attempts.");
