using System.Collections.Concurrent;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Repositories;

namespace UrlShortener.Tests.TestDoubles;

/// <summary>
/// Faithful copy of the buggy pattern from the brownfield report: "if not exists then insert".
/// The await between check and write is where a real database round-trip would sit; a barrier holds every caller
/// there until all <paramref name="expectedCallers"/> have checked, so the interleaving is deterministic, not timing luck.
/// Kept only to reproduce the bug in <c>CollisionBugReproductionTests</c>.
/// </summary>
public sealed class CheckThenInsertLinkRepository(int expectedCallers) : ILinkRepository
{
    private readonly ConcurrentDictionary<string, ShortLink> _links = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _allChecked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checked;
    private int _successfulInserts;

    public int SuccessfulInserts => Volatile.Read(ref _successfulInserts);

    public async ValueTask<bool> TryAddAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        if (_links.ContainsKey(link.Code))
        {
            return false;
        }

        // e.g. SELECT … then INSERT on a separate round-trip
        if (Interlocked.Increment(ref _checked) == expectedCallers)
        {
            _allChecked.SetResult();
        }

        await _allChecked.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        _links[link.Code] = link; // last writer wins: an earlier caller's link is silently replaced
        Interlocked.Increment(ref _successfulInserts);
        return true;
    }

    public ValueTask<ShortLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_links.TryGetValue(code, out var l) ? l : null);

    public ValueTask UpdateAsync(ShortLink link, CancellationToken cancellationToken = default)
    {
        _links[link.Code] = link;
        return ValueTask.CompletedTask;
    }
}
