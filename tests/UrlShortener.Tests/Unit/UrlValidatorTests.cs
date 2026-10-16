using Microsoft.Extensions.Options;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Services;

namespace UrlShortener.Tests.Unit;

public class UrlValidatorTests
{
    private readonly UrlValidator _sut = new(Options.Create(new ShortenerOptions { MaxUrlLength = 100 }));

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1#frag")]
    [InlineData("https://sub.example.co.uk:8443/a/b")]
    [InlineData("  https://example.com/trimmed  ")]
    [InlineData("https://8.8.8.8/public-ip")]
    public void Validate_AcceptsPublicHttpAndHttpsUrls(string url)
    {
        var uri = _sut.Validate(url);

        Assert.True(uri.IsAbsoluteUri);
        Assert.Contains(uri.Scheme, new[] { "http", "https" });
    }

    [Theory]
    [InlineData(null, "required")]
    [InlineData("", "required")]
    [InlineData("   ", "required")]
    [InlineData("example.com", "absolute")]
    [InlineData("/relative/path", "absolute")]
    [InlineData("ftp://example.com/file", "http and https")]
    [InlineData("javascript:alert(1)", "http and https")]
    [InlineData("file:///C:/Windows/win.ini", "http and https")]
    [InlineData("https://user:pass@example.com", "credentials")]
    public void Validate_RejectsMalformedOrDangerousUrls(string? url, string expectedMessagePart)
    {
        var ex = Assert.Throws<InvalidUrlException>(() => _sut.Validate(url));

        Assert.Contains(expectedMessagePart, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://api.localhost")]
    [InlineData("http://printer.local")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://10.1.2.3")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://192.168.1.10")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://0.0.0.0")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("http://localhost./")]                  // trailing root dot
    [InlineData("http://LOCALHOST/")]
    [InlineData("http://[fe80::1%25eth0]/")]            // IPv6 link-local with zone id
    [InlineData("http://2130706433/")]                  // decimal 127.0.0.1
    [InlineData("http://0x7f000001/")]                  // hex 127.0.0.1
    [InlineData("http://0177.0.0.1/")]                  // octal 127.0.0.1
    [InlineData("http://127.1/")]                       // short form
    [InlineData("http://100.64.0.1/")]                  // CGNAT
    [InlineData("http://198.18.0.1/")]                  // benchmarking
    [InlineData("http://224.0.0.1/")]                   // multicast
    [InlineData("http://255.255.255.255/")]             // broadcast
    [InlineData("http://[ff02::1]/")]                   // IPv6 multicast
    [InlineData("http://[64:ff9b::a00:1]/")]            // NAT64 → 10.0.0.1
    [InlineData("http://[2002:c0a8:101::1]/")]          // 6to4 → 192.168.1.1
    [InlineData("http://intranet/")]                    // single-label host
    [InlineData("http://router.home.arpa/")]
    [InlineData("http://metadata.google.internal/")]
    public void Validate_RejectsLoopbackAndPrivateNetworkTargets(string url)
    {
        var ex = Assert.Throws<InvalidUrlException>(() => _sut.Validate(url));

        Assert.Contains("private", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://172.15.0.1")]
    [InlineData("http://172.32.0.1")]
    [InlineData("http://100.63.255.255")]
    [InlineData("http://100.128.0.1")]
    [InlineData("http://198.17.255.255")]
    [InlineData("http://223.255.255.255")]
    [InlineData("http://[2001:4860:4860::8888]/")]
    [InlineData("http://[64:ff9b::808:808]/")]          // NAT64 → 8.8.8.8 (public)
    public void Validate_AllowsAddressesJustOutsidePrivateRanges(string url)
    {
        Assert.NotNull(_sut.Validate(url));
    }

    [Fact]
    public void Validate_RejectsUrlsLongerThanConfiguredMaximum()
    {
        var url = "https://example.com/" + new string('a', 100);

        var ex = Assert.Throws<InvalidUrlException>(() => _sut.Validate(url));

        Assert.Contains("at most 100", ex.Message);
    }

    [Fact]
    public void Validate_RejectsLinksToTheShortenerItself_ToPreventRedirectLoops()
    {
        var sut = new UrlValidator(Options.Create(new ShortenerOptions { BaseUrl = "https://sho.rt" }));

        var ex = Assert.Throws<InvalidUrlException>(() => sut.Validate("https://SHO.RT./abc1234"));

        Assert.Contains("redirect loop", ex.Message);
    }

    [Fact]
    public void Validate_RejectsImplicitFilePath_OnEveryOperatingSystem()
    {
        // Regression: on Linux, Uri.TryCreate("/relative/path", Absolute) succeeds as file:///relative/path.
        var ex = Assert.Throws<InvalidUrlException>(() => _sut.Validate("/etc/passwd"));

        Assert.Contains("absolute", ex.Message);
    }
}
