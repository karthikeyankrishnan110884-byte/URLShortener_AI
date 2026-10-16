using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;
using UrlShortener.Tests.TestDoubles;

namespace UrlShortener.Tests.Unit;

public class DomainDenylistScreenTests
{
    [Theory]
    [InlineData("https://evil.test/", true)]
    [InlineData("https://a.b.EVIL.test./x", true)]
    [InlineData("https://notevil.test/", false)]
    [InlineData("https://evil.test.example.com/", false)]
    [InlineData("https://example.com/?u=evil.test", false)]
    public void Blocks_DomainAndSubdomains_Only(string url, bool blocked)
    {
        var sut = new DomainDenylistScreen(new StaticOptionsMonitor<SafetyOptions>(new SafetyOptions { BlockedDomains = ["Evil.Test", " "] }));

        Assert.Equal(blocked, sut.GetBlockReason(new Uri(url)) is not null);
    }

    [Fact]
    public void EmptyList_BlocksNothing()
    {
        var sut = new DomainDenylistScreen(new StaticOptionsMonitor<SafetyOptions>(new SafetyOptions()));

        Assert.Null(sut.GetBlockReason(new Uri("https://anything.example")));
    }

    [Fact]
    public void ConfigReload_TakesEffectWithoutRestart()
    {
        var monitor = new StaticOptionsMonitor<SafetyOptions>(new SafetyOptions());
        var sut = new DomainDenylistScreen(monitor);
        var target = new Uri("https://newly-bad.test/");

        Assert.Null(sut.GetBlockReason(target));
        monitor.Set(new SafetyOptions { BlockedDomains = ["newly-bad.test"] });

        Assert.NotNull(sut.GetBlockReason(target));
    }
}
