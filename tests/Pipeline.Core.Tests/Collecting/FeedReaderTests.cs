using System.Text;
using Pipeline.Core.Collecting;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Tests.Collecting;

public class FeedReaderTests
{
    private static Stream Fixture() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "evrensel-haber.xml"));

    private static Stream Text(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void ReadsLinkTitleAndDateOfEachItem()
    {
        var articles = FeedReader.Read(Fixture());

        Assert.Equal(3, articles.Count);
        var first = articles[0];
        // The guid: the link without the feed's tracking parameters.
        Assert.Equal("https://www.evrensel.net/haber/6005816/avukat-afsin-hatipoglu-canli-yayinda-tehdit-edildim", first.Url);
        Assert.Equal("Avukat Afşin Hatipoğlu: canlı yayında tehdit edildim", first.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 17, 16, 0, TimeSpan.FromHours(3)), first.PublishedAt);
        Assert.Null(first.Content);
    }

    [Fact]
    public void FallsBackToTheLinkAndSkipsItemsWithoutOne()
    {
        var articles = FeedReader.Read(Text("""
            <rss version="2.0"><channel>
              <item><title>Linkli</title><link>https://www.evrensel.net/haber/1/a?utm_source=rss</link><guid isPermaLink="false">abc-123</guid><pubDate>yesterday</pubDate></item>
              <item><title>Linksiz</title></item>
            </channel></rss>
            """));

        var article = Assert.Single(articles);
        Assert.Equal("https://www.evrensel.net/haber/1/a?utm_source=rss", article.Url);
        Assert.Null(article.PublishedAt); // unreadable date: the page's own date is used
    }

    [Fact]
    public void OnlyEvrenselsIsciSendikaSectionIsTaken()
    {
        var evrensel = SourceCatalog.All.Single(s => s.Id == "evrensel");

        var (taken, outside) = FeedReader.ReadAll(Fixture(), section: evrensel.FeedSection);

        // The fixture's third item is "İşçi̇-Sendi̇ka", written with stray combining dots.
        var article = Assert.Single(taken);
        Assert.Equal(2, outside);
        Assert.Equal(FeedReader.Read(Fixture())[2].Url, article.Url);
    }

    [Fact]
    public void ACategoryAmongSeveralCounts()
    {
        var section = new FeedSection("İşçi-Sendika", Categories: ["İşçi-Sendika"]);

        Assert.True(section.Matches(["Güncel,İŞÇİ-SENDİKA"], "https://x/1"));
        Assert.False(section.Matches(["Güncel", "Ekonomi̇"], "https://x/1"));
        Assert.False(section.Matches([], "https://x/1"));
    }

    [Fact]
    public void OnlyKizilbayraksSinifSectionIsTaken()
    {
        var kizilbayrak = SourceCatalog.All.Single(s => s.Id == "kizilbayrak");
        using var feed = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "kizilbayrak-rss.xml"));

        var (taken, outside) = FeedReader.ReadAll(feed, withText: true, kizilbayrak.FeedSection);

        Assert.Equal("https://kizilbayrak85.net/ana-sayfa/haber/sinif/sokaga-cikma-sirasi-kimde", Assert.Single(taken).Url);
        Assert.Equal(1, outside); // the Kadın item
        Assert.True(kizilbayrak.FeedSection!.Matches([], "https://kizilbayrak85.net/ana-sayfa/haber/sinif/sinboda-direnis-suruyor-4"));
        Assert.False(kizilbayrak.FeedSection.Matches([], "https://kizilbayrak85.net/ana-sayfa/haber/sinifsal-bir-yazi"));
    }

    [Theory]
    [InlineData("<html><body>Bakım çalışması</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x "y">]><rss><channel/></rss>""")]
    public void RefusesWhatIsNotAnRssFeed(string body) =>
        Assert.Throws<FormatException>(() => FeedReader.Read(Text(body)));

    [Fact]
    public void TakesTheWholeTextWhenTheFeedCarriesIt()
    {
        using var feed = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "kizilbayrak-rss.xml"));

        var articles = FeedReader.Read(feed, withText: true);

        Assert.Equal(2, articles.Count);
        var first = articles[0];
        // The guid is only a slug, so the link is used; mirror domains are mapped later by the canonicaliser.
        Assert.Equal("https://kizilbayrak85.net/ana-sayfa/haber/kadin/kadin-adalet-ve-hafiza-nobeti-suruyor", first.Url);
        Assert.Equal("Kadın, Adalet ve Hafıza Nöbeti sürüyor", first.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 23, 30, 0, TimeSpan.FromHours(3)), first.PublishedAt);
        var paragraphs = first.Content!.Split("\n\n");
        // The bold lead repeated as the first body paragraph counts once; the quote's paragraph once; no image.
        Assert.Equal(4, paragraphs.Length);
        Assert.StartsWith("Diyarbakır’da Şiddetle Mücadele Ağı", paragraphs[0]);
        Assert.StartsWith("“Biz kadınların yaşarken korunmasını", paragraphs[2]);
        Assert.DoesNotContain("<", first.Content);
        Assert.StartsWith("Avrupa Gündemi’nde bu hafta", articles[1].Content);
    }

    [Fact]
    public void WithoutTextOnlyTheLinkTitleAndDate() =>
        Assert.All(FeedReader.Read(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "kizilbayrak-rss.xml"))),
            a => Assert.Null(a.Content));

    [Theory]
    [InlineData("<p>Bir.</p><ul><li>iki</li><li>üç</li></ul>", "Bir.\n\n- iki\n\n- üç")]
    [InlineData("Düz metin, &#252;st&#252;nde etiket yok.", "Düz metin, üstünde etiket yok.")]
    [InlineData("<img src=\"x.jpg\" />", null)]
    public void FeedHtmlBecomesParagraphs(string html, string? text) => Assert.Equal(text, FeedText.ToParagraphs(html));
}
