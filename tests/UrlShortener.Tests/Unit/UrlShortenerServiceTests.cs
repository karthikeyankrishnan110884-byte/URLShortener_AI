using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UrlShortener.Api.Contracts;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Repositories;
using UrlShortener.Api.Services;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public class UrlShortenerServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryLinkRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly ShortenerOptions _options = new() { CodeLength = 7, MaxCodeGenerationAttempts = 3 };

    private UrlShortenerService CreateSut(IShortCodeGenerator? generator = null) => new(
        _repository,
        generator ?? new RandomShortCodeGenerator(),
        new UrlValidator(Options.Create(_options)),
        _clock,
        Options.Create(_options),
        NullLogger<UrlShortenerService>.Instance);

    // ---------- Create ----------

    [Fact]
    public void Create_WithValidUrl_StoresActiveLinkWithGeneratedCode()
    {
        var sut = CreateSut();

        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com/docs" });

        Assert.Equal(7, link.Code.Length);
        Assert.Equal("https://example.com/docs", link.OriginalUrl.AbsoluteUri);
        Assert.Equal(Start, link.CreatedAt);
        Assert.True(link.IsActive);
        Assert.False(link.IsCustomAlias);
        Assert.Same(link, _repository.GetByCode(link.Code));
    }

    [Fact]
    public void Create_SameUrlTwice_ProducesTwoIndependentLinks()
    {
        // Decision: no de-duplication, so each creator gets separate analytics and lifecycle.
        var sut = CreateSut();

        var first = sut.Create(new CreateLinkRequest { Url = "https://example.com" });
        var second = sut.Create(new CreateLinkRequest { Url = "https://example.com" });

        Assert.NotEqual(first.Code, second.Code);
    }

    [Fact]
    public void Create_WhenGeneratedCodeCollides_RetriesAndSucceeds()
    {
        _repository.TryAdd(new ShortLink("TAKEN01", new Uri("https://existing.example"), Start, null, false));
        var generator = new SequenceCodeGenerator("TAKEN01", "TAKEN01", "FRESH01");
        var sut = CreateSut(generator);

        var link = sut.Create(new CreateLinkRequest { Url = "https://new.example" });

        Assert.Equal("FRESH01", link.Code);
        Assert.Equal(3, generator.Calls);
        Assert.Equal("https://existing.example/", _repository.GetByCode("TAKEN01")!.OriginalUrl.AbsoluteUri);
    }

    [Fact]
    public void Create_WhenEveryAttemptCollides_FailsAfterConfiguredAttempts()
    {
        _repository.TryAdd(new ShortLink("TAKEN01", new Uri("https://existing.example"), Start, null, false));
        var generator = new SequenceCodeGenerator("TAKEN01");
        var sut = CreateSut(generator);

        Assert.Throws<CodeGenerationException>(() => sut.Create(new CreateLinkRequest { Url = "https://new.example" }));
        Assert.Equal(_options.MaxCodeGenerationAttempts, generator.Calls);
    }

    [Fact]
    public void Create_WithCustomAlias_UsesAliasAsCode()
    {
        var link = CreateSut().Create(new CreateLinkRequest { Url = "https://example.com", CustomAlias = " promo-2026 " });

        Assert.Equal("promo-2026", link.Code);
        Assert.True(link.IsCustomAlias);
    }

    [Fact]
    public void Create_WithAliasAlreadyInUse_ThrowsConflict()
    {
        var sut = CreateSut();
        sut.Create(new CreateLinkRequest { Url = "https://a.example", CustomAlias = "sale" });

        Assert.Throws<AliasConflictException>(() =>
            sut.Create(new CreateLinkRequest { Url = "https://b.example", CustomAlias = "sale" }));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("emoji😀")]
    [InlineData("slash/path")]
    [InlineData("this-alias-is-way-too-long-to-be-accepted")]
    public void Create_WithMalformedAlias_ThrowsInvalidAlias(string alias)
    {
        Assert.Throws<InvalidAliasException>(() =>
            CreateSut().Create(new CreateLinkRequest { Url = "https://example.com", CustomAlias = alias }));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("Swagger")]
    [InlineData("HEALTH")]
    public void Create_WithReservedAlias_IsRejected_SoRoutesCannotBeHijacked(string alias)
    {
        var ex = Assert.Throws<InvalidAliasException>(() =>
            CreateSut().Create(new CreateLinkRequest { Url = "https://example.com", CustomAlias = alias }));

        Assert.Contains("reserved", ex.Message);
    }

    [Fact]
    public void Create_WithExpiryInThePast_IsRejected()
    {
        Assert.Throws<InvalidExpiryException>(() =>
            CreateSut().Create(new CreateLinkRequest { Url = "https://example.com", ExpiresAt = Start.AddMinutes(-1) }));
    }

    [Fact]
    public void Create_WithUnsafeUrl_DoesNotStoreAnything()
    {
        Assert.Throws<InvalidUrlException>(() =>
            CreateSut().Create(new CreateLinkRequest { Url = "http://169.254.169.254/", CustomAlias = "meta" }));

        Assert.Null(_repository.GetByCode("meta"));
    }

    // ---------- Resolve ----------

    [Fact]
    public void ResolveAndTrack_ReturnsTargetAndRecordsClick()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com/landing" });

        var target = sut.ResolveAndTrack(link.Code, "https://www.LinkedIn.com/feed/", "Mozilla/5.0");

        Assert.Equal("https://example.com/landing", target.AbsoluteUri);
        Assert.Equal(1, link.ClickCount);
        var click = Assert.Single(_repository.GetClicks(link.Code));
        Assert.Equal("www.linkedin.com", click.ReferrerHost);
        Assert.Equal("Mozilla/5.0", click.UserAgent);
    }

    [Fact]
    public void ResolveAndTrack_UnknownCode_ThrowsNotFound()
    {
        Assert.Throws<LinkNotFoundException>(() => CreateSut().ResolveAndTrack("nope123", null, null));
    }

    [Fact]
    public void ResolveAndTrack_AfterExpiry_ThrowsGone_AndDoesNotCountClick()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com", ExpiresAt = Start.AddHours(1) });

        _clock.Advance(TimeSpan.FromHours(1));

        var ex = Assert.Throws<LinkGoneException>(() => sut.ResolveAndTrack(link.Code, null, null));
        Assert.Contains("expired", ex.Message);
        Assert.Equal(0, link.ClickCount);
    }

    [Fact]
    public void ResolveAndTrack_AfterDeactivation_ThrowsGone()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com" });

        sut.Deactivate(link.Code);

        var ex = Assert.Throws<LinkGoneException>(() => sut.ResolveAndTrack(link.Code, null, null));
        Assert.Contains("deactivated", ex.Message);
    }

    [Fact]
    public void ResolveAndTrack_TruncatesOversizedUserAgent_AndIgnoresInvalidReferrer()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com" });

        sut.ResolveAndTrack(link.Code, "not a url", new string('x', 5_000));

        var click = Assert.Single(_repository.GetClicks(link.Code));
        Assert.Null(click.ReferrerHost);
        Assert.Equal(256, click.UserAgent!.Length);
    }

    // ---------- Stats & lifecycle ----------

    [Fact]
    public void GetStats_AggregatesClicksByDayAndTopReferrers()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com" });

        sut.ResolveAndTrack(link.Code, "https://google.com/search", null);
        sut.ResolveAndTrack(link.Code, "https://google.com/other", null);
        sut.ResolveAndTrack(link.Code, null, null);
        _clock.Advance(TimeSpan.FromDays(1));
        sut.ResolveAndTrack(link.Code, "https://twitter.com/x", null);

        var stats = sut.GetStats(link.Code);

        Assert.Equal(4, stats.TotalClicks);
        Assert.Equal(Start.AddDays(1), stats.LastAccessedAt);
        Assert.Equal(
            new[] { new DailyClicks(new DateOnly(2026, 10, 10), 3), new DailyClicks(new DateOnly(2026, 10, 11), 1) },
            stats.ClicksByDay);
        Assert.Equal(new ReferrerClicks("google.com", 2), stats.TopReferrers[0]);
        Assert.Contains(new ReferrerClicks("(direct)", 1), stats.TopReferrers);
    }

    [Fact]
    public void GetStats_ForNewLink_IsEmptyNotNull()
    {
        var sut = CreateSut();
        var link = sut.Create(new CreateLinkRequest { Url = "https://example.com" });

        var stats = sut.GetStats(link.Code);

        Assert.Equal(0, stats.TotalClicks);
        Assert.Null(stats.LastAccessedAt);
        Assert.Empty(stats.ClicksByDay);
        Assert.Empty(stats.TopReferrers);
    }

    [Fact]
    public void Deactivate_UnknownCode_ThrowsNotFound()
    {
        Assert.Throws<LinkNotFoundException>(() => CreateSut().Deactivate("missing"));
    }
}
