using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Storage;
using Pipeline.Core.Tests;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Tests.Submission;

public class ArticleSubmissionTests
{
    private const string EvrenselUrl = "https://www.evrensel.net/haber/6005412/x?utm_source=rss_feed";

    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly RecordingQueue _queue = new();
    private readonly ArticleSubmission _submission;

    public ArticleSubmissionTests() => _submission = new(_ledger, _parsed, _queue, TimeProvider.System);

    private Task<SubmitArticlesResponse> Submit(ArticleOrigin origin = ArticleOrigin.Manual, params ArticleToSubmit[] articles) =>
        _submission.SubmitAsync(new SubmitArticlesRequest(articles, origin));

    [Fact]
    public async Task NewEvrenselArticleIsRecordedAndQueued()
    {
        var response = await Submit(ArticleOrigin.Csv, new ArticleToSubmit(EvrenselUrl, "Başlık", new DateTimeOffset(2026, 10, 17, 22, 40, 0, TimeSpan.FromHours(3))));

        var result = Assert.Single(response.Results);
        Assert.Equal(SubmissionStatus.Accepted, result.Status);
        Assert.Equal(ArticleIdentity.FromUrl(EvrenselUrl), result.ArticleId);

        var record = _ledger.Records[result.ArticleId!];
        Assert.Equal("evrensel", record.SourceId);
        Assert.Equal(TurkeyTime.Offset, record.ListDate!.Value.Offset); // submission time until the date is known
        Assert.Equal(ArticleOrigin.Csv, record.Origin);
        Assert.Equal(StageStatus.Succeeded, record.Stages[Stage.Submitted].Status);

        var (queueName, message) = Assert.Single(_queue.Sent);
        Assert.Equal(QueueNames.ArticleSubmitted, queueName);
        var submitted = Assert.IsType<ArticleSubmitted>(message);
        Assert.Equal(EvrenselUrl, submitted.Url);                                  // original, for fetching
        Assert.Equal("https://evrensel.net/haber/6005412/x", submitted.CanonicalUrl); // identity
        Assert.Equal("Başlık", submitted.Title);
        Assert.NotNull(submitted.PublishedAt);
    }

    [Fact]
    public async Task SameArticleAgainIsADuplicateAndNotQueuedTwice()
    {
        await Submit(articles: new ArticleToSubmit(EvrenselUrl));

        // A variant of the same link: same ArticleId.
        var response = await Submit(articles: new ArticleToSubmit("http://evrensel.net/haber/6005412/x/#yorumlar"));

        Assert.Equal(SubmissionStatus.Duplicate, Assert.Single(response.Results).Status);
        Assert.Single(_queue.Sent);
    }

    [Fact]
    public async Task RepeatWithinOneBatchIsADuplicate()
    {
        var response = await Submit(articles: [new ArticleToSubmit(EvrenselUrl), new ArticleToSubmit(EvrenselUrl)]);

        Assert.Equal([SubmissionStatus.Accepted, SubmissionStatus.Duplicate], response.Results.Select(r => r.Status));
        Assert.Single(_queue.Sent);
    }

    [Theory]
    [InlineData("not a url", "Not an absolute http(s) URL")]
    [InlineData("", "Not an absolute http(s) URL")]
    [InlineData("https://example.org/news/1", "Unknown source")]
    [InlineData("https://www.birgun.net/haber/x-1", "Source 'birgun' is not enabled yet")]
    [InlineData("https://birgun.net/haber/x-1", "Source 'birgun' is not enabled yet")]
    public async Task RejectedArticlesLeaveNoTrace(string url, string reason)
    {
        var result = Assert.Single((await Submit(articles: new ArticleToSubmit(url))).Results);

        Assert.Equal(SubmissionStatus.Rejected, result.Status);
        Assert.Equal(reason, result.Reason);
        Assert.Empty(_ledger.Records);
        Assert.Empty(_queue.Sent);
    }

    [Fact]
    public async Task MixedBatchKeepsOrder()
    {
        var response = await Submit(articles:
        [
            new ArticleToSubmit(EvrenselUrl),
            new ArticleToSubmit("https://www.birgun.net/haber/x-1"),
            new ArticleToSubmit("https://www.evrensel.net/haber/6005413/y"),
        ]);

        Assert.Equal(
            [SubmissionStatus.Accepted, SubmissionStatus.Rejected, SubmissionStatus.Accepted],
            response.Results.Select(r => r.Status));
        Assert.Equal(2, _queue.Sent.Count);
    }

    [Fact]
    public async Task ArticleWithContentEntersAlreadyParsed()
    {
        var date = new DateTimeOffset(2026, 10, 17, 22, 40, 0, TimeSpan.FromHours(3));
        var response = await Submit(ArticleOrigin.Csv, new ArticleToSubmit(EvrenselUrl, " Başlık ", date, "  Gövde metni.  "));

        var result = Assert.Single(response.Results);
        Assert.Equal(SubmissionStatus.Accepted, result.Status);

        var parsed = Assert.Single(_parsed.Saved);
        Assert.Equal(result.ArticleId, parsed.ArticleId);
        Assert.Equal("provided:csv", parsed.ParserVersion);
        Assert.Null(parsed.RawHtmlPath);
        Assert.Equal("Başlık", parsed.Title);
        Assert.Equal(date, parsed.PublishedAt);
        Assert.Equal("Gövde metni.", parsed.Body);

        var record = _ledger.Records[result.ArticleId!];
        Assert.Equal([Stage.Submitted, Stage.Parsed], record.Stages.Keys.Order());
        Assert.Equal("Başlık", record.Title);   // copied for the inbox list
        Assert.Equal(date, record.PublishedAt);
        Assert.Equal(date, record.ListDate);
        Assert.Equal("provided:csv", record.Stages[Stage.Parsed].Version);

        // Skips Fetch: goes straight to the queue after parsing.
        var (queueName, message) = Assert.Single(_queue.Sent);
        Assert.Equal(QueueNames.ArticleParsed, queueName);
        Assert.Equal(parsed.Id, Assert.IsType<ArticleParsed>(message).ParsedId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankContentMeansFetchThePage(string? content)
    {
        await Submit(articles: new ArticleToSubmit(EvrenselUrl, "Başlık", null, content));

        Assert.Empty(_parsed.Saved);
        Assert.Equal(QueueNames.ArticleSubmitted, Assert.Single(_queue.Sent).Queue);
    }

    [Fact]
    public async Task DuplicateWithContentAddsNothing()
    {
        await Submit(articles: new ArticleToSubmit(EvrenselUrl));

        var response = await Submit(ArticleOrigin.Csv, new ArticleToSubmit(EvrenselUrl, "Başlık", null, "Gövde"));

        Assert.Equal(SubmissionStatus.Duplicate, Assert.Single(response.Results).Status);
        Assert.Empty(_parsed.Saved);
        Assert.Single(_queue.Sent);
    }
}
