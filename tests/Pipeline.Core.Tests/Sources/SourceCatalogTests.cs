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
    public void EvrenselSendikaAndKizilbayrakAreEnabled() =>
        Assert.Equal(["evrensel", "sendika", "kizilbayrak"], SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id));

    [Fact]
    public void SendikaIsCollectedFromItsSectionPageAndTheOthersFromFeeds()
    {
        Assert.Equal(["evrensel", "sendika", "kizilbayrak"], SourceCatalog.All.Where(s => s.IsCollected).Select(s => s.Id));
        Assert.Equal(["sendika"], SourceCatalog.All.Where(s => s.SectionPage is not null).Select(s => s.Id));
    }

    [Theory]
    [InlineData("https://sendika.org/2026/10/imbat-madencilik-iscileri-direnisin-besinci-gununde-754962", true)]
    [InlineData("https://sendika.org/2026/10/imbat-madencilik-iscileri-direnisin-besinci-gununde-754962/", true)]
    [InlineData("https://sendika.org/category/emek-sermaye", false)]
    [InlineData("https://sendika.org/yazarlar/ufuk-akkus", false)]
    [InlineData("https://example.org/2026/10/x-754962", false)]
    public void SendikasSectionPageKnowsAnArticleLink(string url, bool isArticle) =>
        Assert.Equal(isArticle, SourceCatalog.All.Single(s => s.Id == "sendika").SectionPage!.IsArticle(new Uri(url)));

    [Fact]
    public void OnlyKizilbayraksFeedCarriesTheWholeText() =>
        Assert.Equal(["kizilbayrak"], SourceCatalog.All.Where(s => s.FeedHasFullText).Select(s => s.Id));

    [Fact]
    public void UnknownHostIsNotResolved() =>
        Assert.False(SourceCatalog.TryResolve("https://example.org/x", out _));
}
