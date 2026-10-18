using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Storage;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Tests.Submission;

public class ArticleSubmissionTests
{
    private const string EvrenselUrl = "https://www.evrensel.net/haber/6005412/x?utm_source=rss_feed";

    private readonly InMemoryLedger _ledger = new();
    private readonly RecordingQueue _queue = new();
    private readonly ArticleSubmission _submission;

    public ArticleSubmissionTests() => _submission = new(_ledger, _queue, TimeProvider.System);

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
    [InlineData("https://kizilbayrak85.net/ana-sayfa/haber/guncel/x", "Source 'kizilbayrak' is not enabled yet")]
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

    private sealed class InMemoryLedger : ILedgerStore
    {
        public Dictionary<string, LedgerRecord> Records { get; } = [];

        public Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default) =>
            Task.FromResult(Records.TryAdd(record.Id, record));

        public Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default) =>
            Task.FromResult(Records.GetValueOrDefault(articleId));

        public Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingQueue : IQueueSender
    {
        public List<(string Queue, object? Message)> Sent { get; } = [];

        public Task SendAsync<T>(string queueName, T message, CancellationToken ct = default)
        {
            Sent.Add((queueName, message));
            return Task.CompletedTask;
        }
    }
}
