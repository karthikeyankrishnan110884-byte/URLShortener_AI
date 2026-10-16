namespace UrlShortener.Api.Services;

public enum IdempotencyOutcome
{
    /// <summary>First time this key is seen: the caller must create and then <see cref="IIdempotencyStore.Complete"/>.</summary>
    Started,

    /// <summary>The key completed before with the same request: return the stored code.</summary>
    Replay,

    /// <summary>The key was used with a different request body.</summary>
    Mismatch,

    /// <summary>Another request with the key is in flight.</summary>
    InProgress,
}

public readonly record struct IdempotencyResult(IdempotencyOutcome Outcome, string? Code = null);

/// <summary>Remembers Idempotency-Key → created code so client retries of POST don't create duplicates.</summary>
public interface IIdempotencyStore
{
    IdempotencyResult Begin(string key, string fingerprint);

    void Complete(string key, string code);

    /// <summary>Releases a key whose request failed, so the client can retry.</summary>
    void Abandon(string key);
}
