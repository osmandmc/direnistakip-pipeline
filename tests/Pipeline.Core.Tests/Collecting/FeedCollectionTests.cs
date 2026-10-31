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

    private static HttpResponseMessage SendikaPage() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sendika", "emek-sermaye.html"))),
    };

    private static HttpResponseMessage AllSources(HttpRequestMessage request) => request.RequestUri!.Host switch
    {
        var host when host.Contains("evrensel") => Fixture("evrensel-haber.xml"),
        var host when host.Contains("sendika") => SendikaPage(),
        _ => Fixture("kizilbayrak-rss.xml"),
    };

    [Fact]
    public async Task OneRunReadsEveryEnabledFeedAndSectionPage()
    {
        var runs = await Collection(AllSources).RunAsync();

        Assert.Equal(
        [
            new FeedRun("evrensel", 1, 1, 0, 0, OutsideSection: 2),
            new FeedRun("sendika", 2, 2, 0, 0),
            new FeedRun("kizilbayrak", 1, 1, 0, 0, OutsideSection: 1),
        ], runs);
        Assert.Equal(4, _ledger.Records.Count);
    }

    [Fact]
    public async Task AFeedThatFailsIsReportedAndTheOthersStillRun()
    {
        var runs = await Collection(r => r.RequestUri!.Host.Contains("evrensel")
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : AllSources(r)).RunAsync();

        Assert.Equal("evrensel", runs[0].SourceId);
        Assert.Contains("503", runs[0].Error);
        Assert.Equal(new FeedRun("kizilbayrak", 1, 1, 0, 0, OutsideSection: 1), runs[2]);
    }

    [Fact]
    public async Task OneSourceOnRequest()
    {
        var runs = await Collection(AllSources).RunAsync("kizilbayrak");

        Assert.Equal(["kizilbayrak"], runs.Select(r => r.SourceId));
    }
}
