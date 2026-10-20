using System.Net;
using Pipeline.Contracts;
using Pipeline.Core.Collecting;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Tests.Collecting;

public class FeedCollectionTests
{
    private readonly InMemoryLedger _ledger = new();

    private static HttpResponseMessage Fixture(string name) => new(HttpStatusCode.OK)
    {
        Content = new StreamContent(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "feeds", name))),
    };

    private FeedCollection Collection(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new FeedCollector(
        new HttpClient(new StubHttpHandler(respond)),
        new ArticleSubmission(_ledger, new InMemoryParsed(), new RecordingQueue(), TimeProvider.System)));

    private static HttpResponseMessage BothFeeds(HttpRequestMessage request) =>
        request.RequestUri!.Host.Contains("evrensel") ? Fixture("evrensel-haber.xml") : Fixture("kizilbayrak-rss.xml");

    [Fact]
    public async Task OneRunReadsEveryEnabledFeed()
    {
        var runs = await Collection(BothFeeds).RunAsync();

        Assert.Equal([new FeedRun("evrensel", 3, 3, 0, 0), new FeedRun("kizilbayrak", 2, 2, 0, 0)], runs);
        Assert.Equal(5, _ledger.Records.Count);
    }

    [Fact]
    public async Task AFeedThatFailsIsReportedAndTheOthersStillRun()
    {
        var runs = await Collection(r => r.RequestUri!.Host.Contains("evrensel")
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Fixture("kizilbayrak-rss.xml")).RunAsync();

        Assert.Equal("evrensel", runs[0].SourceId);
        Assert.Contains("503", runs[0].Error);
        Assert.Equal(new FeedRun("kizilbayrak", 2, 2, 0, 0), runs[1]);
    }

    [Fact]
    public async Task OneSourceOnRequest()
    {
        var runs = await Collection(BothFeeds).RunAsync("kizilbayrak");

        Assert.Equal(["kizilbayrak"], runs.Select(r => r.SourceId));
    }
}
