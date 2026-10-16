using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// Development/performance-test decorator: adds a fixed delay to every storage call to emulate a network round-trip
/// to a real database. Only registered when <c>Storage:SimulatedLatencyMs</c> &gt; 0 (never in production config).
/// </summary>
public sealed class LatencyInjectingLinkRepository(ILinkRepository inner, TimeSpan latency) : ILinkRepository
{
    public async ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken);
        return await inner.TryAddAsync(link, cancellationToken);
    }

    public async ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken);
        return await inner.GetByCodeAsync(code, cancellationToken);
    }

    public async ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken);
        await inner.UpdateAsync(link, cancellationToken);
    }
}

/// <inheritdoc cref="LatencyInjectingLinkRepository"/>
public sealed class LatencyInjectingClickRepository(IClickRepository inner, TimeSpan latency) : IClickRepository
{
    public async ValueTask AddAsync(ClickEvent click, CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken);
        await inner.AddAsync(click, cancellationToken);
    }

    public async ValueTask AddRangeAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken = default)
    {
        await Task.Delay(latency, cancellationToken); // one round-trip per batch
        await inner.AddRangeAsync(clicks, cancellationToken);
    }

    public ValueTask<ClickSummary> GetSummaryAsync(string code, CancellationToken cancellationToken = default) =>
        inner.GetSummaryAsync(code, cancellationToken);

    public ValueTask<IReadOnlyList<ClickEvent>> GetRecentAsync(string code, CancellationToken cancellationToken = default) =>
        inner.GetRecentAsync(code, cancellationToken);
}
