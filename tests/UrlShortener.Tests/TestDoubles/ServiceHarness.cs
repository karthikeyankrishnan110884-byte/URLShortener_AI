using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Tests.TestDoubles;

/// <summary>Wires the real services over in-memory stores with a fake clock and inline (synchronous) click recording.</summary>
public sealed class ServiceHarness
{
    public static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    public InMemoryLinkRepository Links { get; } = new();

    public InMemoryClickRepository Clicks { get; } = new();

    public FakeTimeProvider Clock { get; } = new(Start);

    public ShortenerOptions Options { get; } = new() { CodeLength = 7, MaxCodeGenerationAttempts = 3, BaseUrl = "https://sho.rt", MaxExpiryDays = 30 };

    public SafetyOptions Safety { get; } = new();

    public UrlShortenerService CreateService(IShortCodeGenerator? generator = null) => new(
        Links,
        generator ?? new RandomShortCodeGenerator(),
        new UrlValidator(Microsoft.Extensions.Options.Options.Create(Options)),
        [new DomainDenylistScreen(new StaticOptionsMonitor<SafetyOptions>(Safety))],
        new InlineClickRecorder(new ClickIngestor(Links, Clicks)),
        new InMemoryIdempotencyStore(Microsoft.Extensions.Options.Options.Create(Options), Clock),
        Clock,
        Microsoft.Extensions.Options.Options.Create(Options),
        NullLogger<UrlShortenerService>.Instance);

    public LinkAnalyticsService CreateAnalytics(int maxDailyRangeDays = 366) => new(
        Links,
        Clicks,
        Clock,
        Microsoft.Extensions.Options.Options.Create(new AnalyticsOptions { MaxDailyRangeDays = maxDailyRangeDays }));
}

/// <summary>Options monitor whose value can be swapped at runtime (simulates a config reload).</summary>
public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    private readonly List<Action<T, string?>> _listeners = [];

    public T CurrentValue { get; private set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Unsubscriber();
    }

    public void Set(T value)
    {
        CurrentValue = value;
        foreach (var listener in _listeners)
        {
            listener(value, null);
        }
    }

    private sealed class Unsubscriber : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
