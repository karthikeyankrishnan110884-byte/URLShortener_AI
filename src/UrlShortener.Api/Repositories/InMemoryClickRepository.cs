using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;

namespace UrlShortener.Api.Repositories;

/// <summary>
/// In-memory analytics store. Aggregates (per day, referrer, UTM tag, device) are updated on write and stay exact;
/// raw events are kept in a bounded window so memory does not grow with traffic.
/// Distinct values per dimension are capped (overflow → "(other)") to bound memory under high-cardinality input.
/// </summary>
public sealed class InMemoryClickRepository(IOptions<AnalyticsOptions> options) : IClickRepository
{
    private readonly AnalyticsOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, LinkClicks> _byCode = new(StringComparer.Ordinal);

    public InMemoryClickRepository()
        : this(Options.Create(new AnalyticsOptions()))
    {
    }

    public ValueTask AddAsync(ClickEvent click, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(click);
        _byCode.GetOrAdd(click.Code, _ => new LinkClicks()).Add(click, _options);
        return ValueTask.CompletedTask;
    }

    public ValueTask AddRangeAsync(IReadOnlyList<ClickEvent> clicks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clicks);
        foreach (var click in clicks)
        {
            _byCode.GetOrAdd(click.Code, _ => new LinkClicks()).Add(click, _options);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ClickSummary> GetSummaryAsync(string code, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_byCode.TryGetValue(code, out var clicks) ? clicks.Snapshot() : ClickSummary.Empty);

    public ValueTask<IReadOnlyList<ClickEvent>> GetRecentAsync(string code, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ClickEvent>>(_byCode.TryGetValue(code, out var clicks) ? clicks.Recent() : []);

    /// <summary>All mutation happens under one lock per link: writes come from a single processor, so contention is low.</summary>
    private sealed class LinkClicks
    {
        private readonly object _gate = new();
        private readonly Queue<ClickEvent> _recent = new();
        private readonly Dictionary<DateOnly, long> _byDay = [];
        private readonly Dictionary<string, long> _byReferrer = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _bySource = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _byMedium = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _byCampaign = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _byDevice = new(StringComparer.OrdinalIgnoreCase);
        private long _total;

        public void Add(ClickEvent click, AnalyticsOptions options)
        {
            var cap = options.MaxDistinctValuesPerDimension;
            lock (_gate)
            {
                _total++;
                var day = DateOnly.FromDateTime(click.OccurredAt.UtcDateTime);
                _byDay[day] = _byDay.GetValueOrDefault(day) + 1;
                Increment(_byReferrer, click.ReferrerHost ?? ClickSummary.Direct, cap);
                Increment(_bySource, click.Utm?.Source ?? ClickSummary.None, cap);
                Increment(_byMedium, click.Utm?.Medium ?? ClickSummary.None, cap);
                Increment(_byCampaign, click.Utm?.Campaign ?? ClickSummary.None, cap);
                Increment(_byDevice, click.Device ?? "unknown", cap);

                if (options.MaxRecentClicksPerLink > 0)
                {
                    _recent.Enqueue(click);
                    while (_recent.Count > options.MaxRecentClicksPerLink)
                    {
                        _recent.Dequeue();
                    }
                }
            }
        }

        public ClickSummary Snapshot()
        {
            lock (_gate)
            {
                return new ClickSummary(
                    _total,
                    new Dictionary<DateOnly, long>(_byDay),
                    new Dictionary<string, long>(_byReferrer, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, long>(_bySource, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, long>(_byMedium, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, long>(_byCampaign, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, long>(_byDevice, StringComparer.OrdinalIgnoreCase));
            }
        }

        public ClickEvent[] Recent()
        {
            lock (_gate)
            {
                return [.. _recent];
            }
        }

        private static void Increment(Dictionary<string, long> counts, string key, int cap)
        {
            if (!counts.ContainsKey(key) && counts.Count >= cap)
            {
                key = ClickSummary.Other;
            }

            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
    }
}
