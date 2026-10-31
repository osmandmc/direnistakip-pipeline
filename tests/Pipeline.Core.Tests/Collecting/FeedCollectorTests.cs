using System.Net;
using Pipeline.Contracts;
using Pipeline.Core.Collecting;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Tests.Collecting;

public class FeedCollectorTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly RecordingQueue _queue = new();
    private readonly Source _evrensel = SourceCatalog.All.Single(s => s.Id == "evrensel");

    private FeedCollector Collector(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(
        new HttpClient(new StubHttpHandler(respond)),
        new ArticleSubmission(_ledger, new InMemoryParsed(), _queue, TimeProvider.System));

    private static HttpResponseMessage Feed() => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "evrensel-haber.xml"))),
    };

    [Fact]
    public async Task SubmitsNewArticlesOnceAcrossRuns()
    {
        var requested = new List<Uri?>();
        var collector = Collector(request => { requested.Add(request.RequestUri); return Feed(); });

        var first = await collector.CollectAsync(_evrensel);
        var second = await collector.CollectAsync(_evrensel);

        // Only the İşçi-Sendika item of the three (B18).
        Assert.Equal(new FeedCollectionResult("evrensel", 1, 1, 0, 0, 2), first);
        Assert.Equal(new FeedCollectionResult("evrensel", 1, 0, 1, 0, 2), second);
        Assert.Equal(new Uri("https://www.evrensel.net/rss/haber.xml"), requested[0]);
        Assert.All(_ledger.Records.Values, r => Assert.Equal(ArticleOrigin.Collector, r.Origin));
        // Queued for Fetch with the feed's title and date.
        var message = Assert.IsType<ArticleSubmitted>(_queue.Sent[0].Message);
        Assert.Equal("DİSK Bursa'dan seslendi: Gelirde, vergide ve ülkede adalet istiyoruz", message.Title);
        Assert.Single(_queue.Sent);
    }

    [Fact]
    public async Task ASourceWithoutAFeedIsCollectedFromItsSectionPage()
    {
        var sendika = SourceCatalog.All.Single(s => s.Id == "sendika");
        var requested = new List<Uri?>();
        var collector = Collector(request =>
        {
            requested.Add(request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sendika", "emek-sermaye.html"))),
            };
        });

        var first = await collector.CollectAsync(sendika);
        var second = await collector.CollectAsync(sendika);

        Assert.Equal(new FeedCollectionResult("sendika", 2, 2, 0, 0), first);
        Assert.Equal(new FeedCollectionResult("sendika", 2, 0, 2, 0), second);
        Assert.Equal(new Uri("https://sendika.org/category/emek-sermaye"), requested[0]);
        // Queued for Fetch: the page is downloaded and parsed like any article.
        var message = Assert.IsType<ArticleSubmitted>(_queue.Sent[0].Message);
        Assert.Equal(("sendika", "Örnek Tekstil işçileri üçüncü gününde"), (message.SourceId, message.Title));
        Assert.Equal(2, _queue.Sent.Count);
    }

    [Fact]
    public async Task ASectionPageThatCannotBeDownloadedThrows()
    {
        var collector = Collector(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => collector.CollectAsync(SourceCatalog.All.Single(s => s.Id == "sendika")));

        Assert.StartsWith("Section page https://sendika.org/category/emek-sermaye answered HTTP 404", error.Message);
    }

    [Fact]
    public async Task AFeedThatCannotBeDownloadedThrows()
    {
        var collector = Collector(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => collector.CollectAsync(_evrensel));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Empty(_ledger.Records);
    }

    [Fact]
    public async Task AFeedWithFullTextEntersAlreadyParsed()
    {
        var kizilbayrak = SourceCatalog.All.Single(s => s.Id == "kizilbayrak");
        var collector = Collector(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", "kizilbayrak-rss.xml"))),
        });

        var result = await collector.CollectAsync(kizilbayrak);

        Assert.Equal(new FeedCollectionResult("kizilbayrak", 1, 1, 0, 0, 1), result); // the Sınıf item; the Kadın one is left out
        Assert.All(_ledger.Records.Values, r =>
        {
            Assert.Equal("kizilbayrak", r.SourceId);
            Assert.Equal("provided:collector", r.Stages[Stage.Parsed].Version);
            Assert.False(r.Stages.ContainsKey(Stage.Fetched)); // the page is not downloaded
        });
        Assert.All(_queue.Sent, m => Assert.IsType<ArticleParsed>(m.Message));
    }
}
