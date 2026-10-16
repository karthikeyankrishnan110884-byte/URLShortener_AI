using UrlShortener.Api.Services.Analytics;

namespace UrlShortener.Tests.Unit;

public class ClickEventFactoryTests
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("", "unknown")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120", "desktop")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) Safari/605", "desktop")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Firefox/121", "desktop")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Mobile/15E148", "mobile")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) Mobile Safari/537.36", "mobile")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X)", "tablet")]
    [InlineData("Mozilla/5.0 (Linux; Android 13; SM-X700) Safari/537.36", "tablet")]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)", "bot")]
    [InlineData("facebookexternalhit/1.1", "bot")]
    [InlineData("curl/8.4.0", "bot")]
    [InlineData("SomethingElse/1.0", "unknown")]
    public void UserAgent_IsClassified(string? ua, string expected)
    {
        Assert.Equal(expected, UserAgentClassifier.Classify(ua));
    }

    [Theory]
    [InlineData("  Newsletter ", "newsletter")]
    [InlineData("spring-sale_2026", "spring-sale_2026")]
    [InlineData("a b+c.d", "a b+c.d")]
    [InlineData("<script>", null)]
    [InlineData("drop;table", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void UtmTag_IsSanitised(string? raw, string? expected)
    {
        Assert.Equal(expected, ClickEventFactory.Tag(raw));
    }

    [Fact]
    public void UtmTag_IsTruncatedTo64Chars()
    {
        Assert.Equal(64, ClickEventFactory.Tag(new string('a', 200))!.Length);
    }

    [Theory]
    [InlineData("https://Example.COM/path?secret=1", "example.com")]
    [InlineData("android-app://com.google.android.gm/", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("garbage", null)]
    public void Referrer_KeepsOnlyHttpHost(string raw, string? expected)
    {
        Assert.Equal(expected, ClickEventFactory.NormalizeReferrer(raw));
    }

    [Fact]
    public void Create_WithNoTags_LeavesUtmNull()
    {
        var click = ClickEventFactory.Create("abc", DateTimeOffset.UnixEpoch, ClickContext.Empty);

        Assert.Null(click.Utm);
        Assert.Equal(UserAgentClassifier.Unknown, click.Device);
    }
}
