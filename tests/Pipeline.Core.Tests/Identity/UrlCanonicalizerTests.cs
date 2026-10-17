using Pipeline.Core.Identity;

namespace Pipeline.Core.Tests.Identity;

/// <summary>The rule table for D-024. Add a row for every new rule or source quirk.</summary>
public class UrlCanonicalizerTests
{
    [Theory]
    // Scheme, host case, www/m/amp prefixes
    [InlineData("http://www.evrensel.net/haber/1/x", "https://evrensel.net/haber/1/x")]
    [InlineData("https://WWW.BirGun.net/haber/x", "https://birgun.net/haber/x")]
    [InlineData("https://m.sendika.org/2026/10/x/", "https://sendika.org/2026/10/x")]
    [InlineData("https://amp.birgun.net/haber/x", "https://birgun.net/haber/x")]
    // Numbered mirror domains map to the source's main host (seen in T4: kizilbayrak.net → kizilbayrak85.net)
    [InlineData("https://kizilbayrak85.net/ana-sayfa/haber/guncel/x", "https://kizilbayrak.net/ana-sayfa/haber/guncel/x")]
    [InlineData("http://www.kizilbayrak54.net/ana-sayfa/haber/guncel/x", "https://kizilbayrak.net/ana-sayfa/haber/guncel/x")]
    [InlineData("https://sendika63.org/2020/01/x-123", "https://sendika.org/2020/01/x-123")]
    // AMP path suffix, trailing slash, fragment, default port
    [InlineData("https://evrensel.net/haber/1/x/amp", "https://evrensel.net/haber/1/x")]
    [InlineData("https://evrensel.net/haber/1/x/#yorumlar", "https://evrensel.net/haber/1/x")]
    [InlineData("https://evrensel.net:443/haber/1/x", "https://evrensel.net/haber/1/x")]
    // Tracking parameters removed, others kept and sorted
    [InlineData("https://birgun.net/haber/x?utm_source=tw&utm_medium=social", "https://birgun.net/haber/x")]
    [InlineData("https://birgun.net/haber/x?fbclid=abc", "https://birgun.net/haber/x")]
    [InlineData("https://kizilbayrak.net/?p=42&utm_campaign=y", "https://kizilbayrak.net?p=42")]
    [InlineData("https://example.org/a?b=2&a=1", "https://example.org/a?a=1&b=2")]
    // Path case is preserved (paths can be case-sensitive)
    [InlineData("https://evrensel.net/Haber/1/X", "https://evrensel.net/Haber/1/X")]
    // Turkish characters are percent-encoded consistently
    [InlineData("https://evrensel.net/haber/1/işçi-direnişi", "https://evrensel.net/haber/1/i%C5%9F%C3%A7i-direni%C5%9Fi")]
    [InlineData("https://evrensel.net/haber/1/i%C5%9F%C3%A7i-direni%C5%9Fi", "https://evrensel.net/haber/1/i%C5%9F%C3%A7i-direni%C5%9Fi")]
    public void Canonicalizes(string input, string expected) =>
        Assert.Equal(expected, UrlCanonicalizer.Canonicalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/haber/1/x")]
    [InlineData("ftp://evrensel.net/x")]
    public void RejectsInvalid(string? input) =>
        Assert.False(UrlCanonicalizer.TryCanonicalize(input, out _));

    [Fact]
    public void VariantsShareArticleId() =>
        Assert.Equal(
            ArticleIdentity.FromUrl("http://www.evrensel.net/haber/1/x/amp?utm_source=tw#top"),
            ArticleIdentity.FromUrl("https://evrensel.net/haber/1/x"));

    [Fact]
    public void ArticleIdIsLowercaseSha256Hex()
    {
        var id = ArticleIdentity.FromUrl("https://evrensel.net/haber/1/x");
        Assert.Matches("^[0-9a-f]{64}$", id);
    }
}
