using UrlShortener.Api.Domain;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Tests.TestDoubles;

/// <summary>Wraps a repository and counts reads, with an optional gate to hold loads in flight (stampede tests).</summary>
public sealed class CountingLinkRepository(ILinkRepository inner) : ILinkRepository
{
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public TaskCompletionSource? Gate { get; set; }

    public ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default) =>
        inner.TryAddAsync(link, cancellationToken);

    public async ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        return await inner.GetByCodeAsync(code, cancellationToken);
    }

    public ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(link, cancellationToken);
}
