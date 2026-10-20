using Pipeline.Core.Identity;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Tests.Sources;

public class SourceCatalogTests
{
    [Theory]
    [InlineData("https://www.evrensel.net/haber/1/x", "evrensel")]
    [InlineData("https://sendika.org/2026/10/x", "sendika")]
    [InlineData("https://kizilbayrak.net/?p=1", "kizilbayrak")]
    [InlineData("https://m.birgun.net/haber/x", "birgun")]
    [InlineData("https://kizilbayrak85.net/ana-sayfa/haber/guncel/x", "kizilbayrak")]
    public void ResolvesKnownSources(string url, string expectedId)
    {
        Assert.True(SourceCatalog.TryResolve(UrlCanonicalizer.Canonicalize(url), out var source));
        Assert.Equal(expectedId, source.Id);
    }

    [Fact]
    public void EvrenselAndKizilbayrakAreEnabled() =>
        Assert.Equal(["evrensel", "kizilbayrak"], SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id));

    [Fact]
    public void OnlyKizilbayraksFeedCarriesTheWholeText() =>
        Assert.Equal(["kizilbayrak"], SourceCatalog.All.Where(s => s.FeedHasFullText).Select(s => s.Id));

    [Fact]
    public void UnknownHostIsNotResolved() =>
        Assert.False(SourceCatalog.TryResolve("https://example.org/x", out _));
}
