using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

/// <summary>
/// Scenario B, step 1: "two different URLs occasionally get the same code".
/// The same concurrent workload runs against the old check-then-insert pattern (bug reproduced)
/// and against the atomic TryAdd contract (bug fixed).
/// </summary>
public class CollisionBugReproductionTests
{
    private const int Concurrency = 20;

    private static UrlShortenerService Service(ILinkRepository repo)
    {
        var options = Options.Create(new ShortenerOptions { MaxCodeGenerationAttempts = 1 });
        var clock = TimeProvider.System;
        return new UrlShortenerService(
            repo,
            new SequenceCodeGenerator("SAME000"), // every request draws the same candidate code
            new UrlValidator(options),
            [],
            new InlineClickRecorder(new ClickIngestor(repo, new InMemoryClickRepository())),
            new InMemoryIdempotencyStore(options, clock),
            clock,
            options,
            NullLogger<UrlShortenerService>.Instance);
    }

    private static async Task<int> CountSuccessfulCreatesAsync(UrlShortenerService sut)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(i => Task.Run(async () =>
        {
            try
            {
                await sut.CreateAsync(new CreateLinkRequest { Url = $"https://site{i}.example" });
                return true;
            }
            catch (CodeGenerationException)
            {
                return false;
            }
        })));
        return results.Count(r => r);
    }

    [Fact]
    public async Task Reproduction_CheckThenInsert_HandsTheSameCodeToSeveralUrls()
    {
        var buggy = new CheckThenInsertLinkRepository(expectedCallers: Concurrency);

        var successes = await CountSuccessfulCreatesAsync(Service(buggy));

        // All 20 callers were told "201 Created, code SAME000", but only one URL is stored: the reported bug.
        Assert.Equal(Concurrency, successes);
        Assert.Equal(Concurrency, buggy.SuccessfulInserts);
    }

    [Fact]
    public async Task Fix_AtomicTryAdd_HandsOutTheCodeExactlyOnce()
    {
        var repo = new InMemoryLinkRepository();

        var successes = await CountSuccessfulCreatesAsync(Service(repo));

        Assert.Equal(1, successes);
        Assert.NotNull(await repo.GetByCodeAsync("SAME000"));
    }
}
