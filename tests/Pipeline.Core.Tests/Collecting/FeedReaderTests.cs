using System.Text;
using Pipeline.Core.Collecting;

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

    [Theory]
    [InlineData("<html><body>Bakım çalışması</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x "y">]><rss><channel/></rss>""")]
    public void RefusesWhatIsNotAnRssFeed(string body) =>
        Assert.Throws<FormatException>(() => FeedReader.Read(Text(body)));
}
