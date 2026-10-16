using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services;
using UrlShortener.Api.Services.Analytics;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public class UrlShortenerServiceTests
{
    private static readonly DateTimeOffset Start = ServiceHarness.Start;

    private readonly ServiceHarness _h = new();

    private UrlShortenerService CreateSut(IShortCodeGenerator? generator = null) => _h.CreateService(generator);

    private static CreateLinkRequest Req(string url, string? alias = null, DateTimeOffset? expiresAt = null) =>
        new() { Url = url, CustomAlias = alias, ExpiresAt = expiresAt };

    private async Task<ShortLink> CreateAsync(UrlShortenerService sut, string url, string? alias = null, DateTimeOffset? expiresAt = null) =>
        (await sut.CreateAsync(Req(url, alias, expiresAt))).Link;

    // ---------- Create ----------

    [Fact]
    public async Task Create_WithValidUrl_StoresActiveLinkWithGeneratedCode()
    {
        var link = await CreateAsync(CreateSut(), "https://example.com/docs");

        Assert.Equal(7, link.Code.Length);
        Assert.Equal("https://example.com/docs", link.OriginalUrl.AbsoluteUri);
        Assert.Equal(Start, link.CreatedAt);
        Assert.True(link.IsActive);
        Assert.False(link.IsCustomAlias);
        Assert.Same(link, await _h.Links.GetByCodeAsync(link.Code));
    }

    [Fact]
    public async Task Create_SameUrlTwice_ProducesTwoIndependentLinks()
    {
        // Decision: no de-duplication, so each creator gets separate analytics and lifecycle.
        var sut = CreateSut();

        var first = await CreateAsync(sut, "https://example.com");
        var second = await CreateAsync(sut, "https://example.com");

        Assert.NotEqual(first.Code, second.Code);
    }

    [Fact]
    public async Task Create_WhenGeneratedCodeCollides_RetriesAndSucceeds()
    {
        await _h.Links.TryAddAsync(new ShortLink("TAKEN01", new Uri("https://existing.example"), Start, null, false));
        var generator = new SequenceCodeGenerator("TAKEN01", "TAKEN01", "FRESH01");

        var link = await CreateAsync(CreateSut(generator), "https://new.example");

        Assert.Equal("FRESH01", link.Code);
        Assert.Equal(3, generator.Calls);
        Assert.Equal("https://existing.example/", (await _h.Links.GetByCodeAsync("TAKEN01"))!.OriginalUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Create_WhenEveryAttemptCollides_FailsAfterConfiguredAttempts()
    {
        await _h.Links.TryAddAsync(new ShortLink("TAKEN01", new Uri("https://existing.example"), Start, null, false));
        var generator = new SequenceCodeGenerator("TAKEN01");

        await Assert.ThrowsAsync<CodeGenerationException>(() => CreateAsync(CreateSut(generator), "https://new.example"));
        Assert.Equal(_h.Options.MaxCodeGenerationAttempts, generator.Calls);
    }

    [Fact]
    public async Task Create_NeverIssuesAGeneratedCodeThatIsAReservedWord()
    {
        var generator = new SequenceCodeGenerator("swagger", "healthz", "GOOD123");

        var link = await CreateAsync(CreateSut(generator), "https://example.com");

        Assert.Equal("GOOD123", link.Code);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsWithScriptedCollisions_AllGetDistinctCodes()
    {
        // Brownfield bug reproduction: many creates racing on the same candidate code must never share it.
        var generator = new SharedCandidateGenerator();
        var sut = CreateSut(generator);

        var links = await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
            Task.Run(async () =>
            {
                try
                {
                    return (await sut.CreateAsync(Req($"https://site{i}.example"))).Link;
                }
                catch (CodeGenerationException)
                {
                    return null;
                }
            })));

        var created = links.Where(l => l is not null).ToList();
        Assert.Equal(created.Count, created.Select(l => l!.Code).Distinct().Count());
        foreach (var link in created)
        {
            Assert.Same(link, await _h.Links.GetByCodeAsync(link!.Code));
        }
    }

    [Fact]
    public async Task Create_WithCustomAlias_UsesAliasAsCode()
    {
        var link = await CreateAsync(CreateSut(), "https://example.com", " promo-2026 ");

        Assert.Equal("promo-2026", link.Code);
        Assert.True(link.IsCustomAlias);
    }

    [Fact]
    public async Task Create_WithAliasAlreadyInUse_ThrowsConflict()
    {
        var sut = CreateSut();
        await CreateAsync(sut, "https://a.example", "sale");

        await Assert.ThrowsAsync<AliasConflictException>(() => CreateAsync(sut, "https://b.example", "sale"));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("emoji😀")]
    [InlineData("slash/path")]
    [InlineData("this-alias-is-way-too-long-to-be-accepted")]
    public async Task Create_WithMalformedAlias_ThrowsInvalidAlias(string alias)
    {
        await Assert.ThrowsAsync<InvalidAliasException>(() => CreateAsync(CreateSut(), "https://example.com", alias));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("Swagger")]
    [InlineData("HEALTH")]
    [InlineData("readyz")]
    public async Task Create_WithReservedAlias_IsRejected_SoRoutesCannotBeHijacked(string alias)
    {
        var ex = await Assert.ThrowsAsync<InvalidAliasException>(() => CreateAsync(CreateSut(), "https://example.com", alias));

        Assert.Contains("reserved", ex.Message);
    }

    [Fact]
    public async Task Create_WithExpiryInThePast_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidExpiryException>(() =>
            CreateAsync(CreateSut(), "https://example.com", expiresAt: Start.AddMinutes(-1)));
    }

    [Fact]
    public async Task Create_WithExpiryBeyondMaximum_IsRejected()
    {
        var ex = await Assert.ThrowsAsync<InvalidExpiryException>(() =>
            CreateAsync(CreateSut(), "https://example.com", expiresAt: Start.AddDays(31)));

        Assert.Contains("30 days", ex.Message);
    }

    [Fact]
    public async Task Create_WithUnsafeUrl_DoesNotStoreAnything()
    {
        await Assert.ThrowsAsync<InvalidUrlException>(() => CreateAsync(CreateSut(), "http://169.254.169.254/", "meta"));

        Assert.Null(await _h.Links.GetByCodeAsync("meta"));
    }

    [Fact]
    public async Task Create_ToDenylistedDomainOrSubdomain_IsRejected()
    {
        _h.Safety.BlockedDomains.Add("evil.test");
        var sut = CreateSut();

        await Assert.ThrowsAsync<UnsafeDestinationException>(() => CreateAsync(sut, "https://login.EVIL.test/reset"));
        await Assert.ThrowsAsync<UnsafeDestinationException>(() => CreateAsync(sut, "https://evil.test/"));
        Assert.NotNull(await CreateAsync(sut, "https://notevil.test/"));
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task Create_WithSameIdempotencyKeyAndBody_ReturnsOriginalLink()
    {
        var sut = CreateSut();

        var first = await sut.CreateAsync(Req("https://example.com/a"), "key-1");
        var retry = await sut.CreateAsync(Req(" https://example.com/a "), "key-1");

        Assert.False(first.Replayed);
        Assert.True(retry.Replayed);
        Assert.Same(first.Link, retry.Link);
    }

    [Fact]
    public async Task Create_WithSameIdempotencyKeyButDifferentBody_IsRejected()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Req("https://example.com/a"), "key-2");

        await Assert.ThrowsAsync<IdempotencyKeyMismatchException>(() => sut.CreateAsync(Req("https://example.com/b"), "key-2"));
    }

    [Fact]
    public async Task Create_FailedRequest_ReleasesIdempotencyKey_SoClientCanRetry()
    {
        var sut = CreateSut();
        await CreateAsync(sut, "https://a.example", "taken");

        await Assert.ThrowsAsync<AliasConflictException>(() => sut.CreateAsync(Req("https://b.example", "taken"), "key-3"));
        await Assert.ThrowsAsync<AliasConflictException>(() => sut.CreateAsync(Req("https://b.example", "taken"), "key-3"));
    }

    // ---------- Resolve ----------

    [Fact]
    public async Task Resolve_ReturnsTargetAndRecordsClick()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com/landing");

        var target = await sut.ResolveAsync(link.Code, new ClickContext("https://www.LinkedIn.com/feed/", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Mobile", "LinkedIn", "Social", "Launch"));

        Assert.Equal("https://example.com/landing", target.AbsoluteUri);
        Assert.Equal(1, link.ClickCount);
        var click = Assert.Single(await _h.Clicks.GetRecentAsync(link.Code));
        Assert.Equal("www.linkedin.com", click.ReferrerHost);
        Assert.Equal(UserAgentClassifier.Mobile, click.Device);
        Assert.Equal(new UtmTags("linkedin", "social", "launch"), click.Utm);
    }

    [Fact]
    public async Task Resolve_UnknownCode_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<LinkNotFoundException>(() => CreateSut().ResolveAsync("nope123", ClickContext.Empty));
    }

    [Fact]
    public async Task Resolve_AfterExpiry_ThrowsGone_AndDoesNotCountClick()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com", expiresAt: Start.AddHours(1));

        _h.Clock.Advance(TimeSpan.FromHours(1));

        var ex = await Assert.ThrowsAsync<LinkGoneException>(() => sut.ResolveAsync(link.Code, ClickContext.Empty));
        Assert.Contains("expired", ex.Message);
        Assert.Equal(0, link.ClickCount);
    }

    [Fact]
    public async Task Resolve_AfterDeactivation_ThrowsGone()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com");

        await sut.DeactivateAsync(link.Code);

        var ex = await Assert.ThrowsAsync<LinkGoneException>(() => sut.ResolveAsync(link.Code, ClickContext.Empty));
        Assert.Contains("deactivated", ex.Message);
    }

    [Fact]
    public async Task Resolve_WhenDomainIsDenylistedAfterCreation_StopsRedirecting()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://later-bad.test/promo");

        _h.Safety.BlockedDomains.Add("later-bad.test");
        var refreshed = CreateSut(); // screen reads options at construction; the real app uses IOptionsMonitor reloads

        var ex = await Assert.ThrowsAsync<LinkGoneException>(() => refreshed.ResolveAsync(link.Code, ClickContext.Empty));
        Assert.Contains("blocked", ex.Message);
        Assert.Equal(0, link.ClickCount);
    }

    [Fact]
    public async Task Peek_ReturnsTarget_WithoutCountingAClick()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com/peek");

        var target = await sut.PeekAsync(link.Code);

        Assert.Equal("https://example.com/peek", target.AbsoluteUri);
        Assert.Equal(0, link.ClickCount);
    }

    [Fact]
    public async Task Resolve_TruncatesOversizedUserAgent_AndIgnoresInvalidReferrerAndTags()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com");

        await sut.ResolveAsync(link.Code, new ClickContext("not a url", new string('x', 5_000), "<script>", null, null));

        var click = Assert.Single(await _h.Clicks.GetRecentAsync(link.Code));
        Assert.Null(click.ReferrerHost);
        Assert.Equal(256, click.UserAgent!.Length);
        Assert.Null(click.Utm);
    }

    // ---------- Lifecycle ----------

    [Fact]
    public async Task Deactivate_IsIdempotent()
    {
        var sut = CreateSut();
        var link = await CreateAsync(sut, "https://example.com");

        await sut.DeactivateAsync(link.Code);
        _h.Clock.Advance(TimeSpan.FromHours(1));
        await sut.DeactivateAsync(link.Code);

        Assert.Equal(Start, link.DeactivatedAt);
    }

    [Fact]
    public async Task Deactivate_UnknownCode_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<LinkNotFoundException>(() => CreateSut().DeactivateAsync("missing"));
    }

    /// <summary>Every caller first proposes the same code, then a unique one: maximises contention on one key.</summary>
    private sealed class SharedCandidateGenerator : IShortCodeGenerator
    {
        private readonly ThreadLocal<int> _calls = new(() => 0);
        private int _unique;

        public string Generate(int length) =>
            _calls.Value++ % 2 == 0 ? "SAME000" : $"U{Interlocked.Increment(ref _unique):000000}";
    }
}
