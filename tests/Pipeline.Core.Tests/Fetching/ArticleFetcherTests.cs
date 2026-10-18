using System.Net;
using Pipeline.Contracts;
using Pipeline.Core.Fetching;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests.Fetching;

public class ArticleFetcherTests
{
    private const string ArticleId = "a1";
    private const string Url = "https://www.evrensel.net/haber/6005412/x";

    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryRawHtml _rawHtml = new();
    private readonly RecordingQueue _queue = new();

    private static readonly ArticleSubmitted Article = new(
        ArticleId, Url, "https://evrensel.net/haber/6005412/x", "evrensel", ArticleOrigin.Manual, DateTimeOffset.UtcNow);

    public ArticleFetcherTests() =>
        _ledger.Records[ArticleId] = new LedgerRecord(
            ArticleId, "evrensel", ArticleOrigin.Manual, Url,
            new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow) });

    private (ArticleFetcher Fetcher, StubHttpHandler Server) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var server = new StubHttpHandler(respond);
        return (new ArticleFetcher(new HttpClient(server), _ledger, _rawHtml, _queue, TimeProvider.System), server);
    }

    private StageResult Fetched => _ledger.Records[ArticleId].Stages[Stage.Fetched];

    [Fact]
    public async Task SavesThePageAndHandsItToParse()
    {
        var (fetcher, _) = Create(_ => StubHttpHandler.Html("<html><article>Metin</article></html>"));

        await fetcher.FetchAsync(Article, attempt: 1);

        Assert.Equal(StageStatus.Succeeded, Fetched.Status);
        var (queueName, message) = Assert.Single(_queue.Sent);
        Assert.Equal(QueueNames.ArticleFetched, queueName);
        var sent = Assert.IsType<ArticleFetched>(message);
        Assert.Equal("<html><article>Metin</article></html>", _rawHtml.Files[sent.RawHtmlPath]);
        Assert.Equal(Url, sent.FinalUrl);
    }

    [Fact]
    public async Task AlreadyFetchedIsSkippedWithoutARequest()
    {
        var (fetcher, server) = Create(_ => StubHttpHandler.Html("<html/>"));
        await fetcher.FetchAsync(Article, attempt: 1);

        await fetcher.FetchAsync(Article, attempt: 1); // the queue delivered the message twice

        Assert.Equal(1, server.Requests);
        Assert.Single(_queue.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    [InlineData(HttpStatusCode.Gone, "HTTP 410")]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403")]
    public async Task PermanentHttpErrorIsRecordedWithoutRetry(HttpStatusCode status, string reason)
    {
        var (fetcher, _) = Create(_ => new HttpResponseMessage(status));

        await fetcher.FetchAsync(Article, attempt: 1); // does not throw: no retry

        Assert.Equal(StageStatus.Failed, Fetched.Status);
        Assert.Equal(reason, Fetched.Reason);
        Assert.Empty(_rawHtml.Files);
        Assert.Empty(_queue.Sent);
    }

    [Fact]
    public async Task NonHtmlIsAPermanentFailure()
    {
        var (fetcher, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("application/pdf") } },
        });

        await fetcher.FetchAsync(Article, attempt: 1);

        Assert.Equal("Not an HTML page (application/pdf)", Fetched.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task TemporaryErrorIsRetried(HttpStatusCode status)
    {
        var (fetcher, _) = Create(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<TemporaryFetchException>(() => fetcher.FetchAsync(Article, attempt: 1));

        Assert.DoesNotContain(Stage.Fetched, _ledger.Records[ArticleId].Stages.Keys);
    }

    [Fact]
    public async Task NetworkErrorAndTimeoutAreRetried()
    {
        var (network, _) = Create(_ => throw new HttpRequestException("connection refused"));
        var (timeout, _) = Create(_ => throw new TaskCanceledException());

        await Assert.ThrowsAsync<TemporaryFetchException>(() => network.FetchAsync(Article, attempt: 2));
        await Assert.ThrowsAsync<TemporaryFetchException>(() => timeout.FetchAsync(Article, attempt: 2));
    }

    [Fact]
    public async Task LastAttemptRecordsTheFailureInsteadOfRetrying()
    {
        var (fetcher, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await fetcher.FetchAsync(Article, attempt: ArticleFetcher.MaxAttempts);

        Assert.Equal(StageStatus.Failed, Fetched.Status);
        Assert.Equal("Gave up after 5 attempts: HTTP 503", Fetched.Reason);
    }

    [Fact]
    public async Task UnknownArticleIsIgnored()
    {
        var (fetcher, server) = Create(_ => StubHttpHandler.Html("<html/>"));

        await fetcher.FetchAsync(Article with { ArticleId = "not-in-ledger" }, attempt: 1);

        Assert.Equal(0, server.Requests);
        Assert.Empty(_queue.Sent);
    }
}
