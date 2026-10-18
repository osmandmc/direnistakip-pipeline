using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Submission;

/// <summary>
/// Takes articles into the pipeline (D-002): one ledger record per ArticleId, then a message
/// for the Fetch stage. Every entry point (manual, CSV, later the RSS timer) goes through here.
/// </summary>
public sealed class ArticleSubmission(ILedgerStore ledger, IQueueSender queue, TimeProvider clock)
{
    public const int MaxBatchSize = 100;

    public async Task<SubmitArticlesResponse> SubmitAsync(SubmitArticlesRequest request, CancellationToken ct = default)
    {
        var results = new List<SubmissionResult>(request.Articles.Count);
        foreach (var article in request.Articles)
        {
            results.Add(await SubmitOneAsync(article, request.Origin, ct));
        }
        return new SubmitArticlesResponse(results);
    }

    private async Task<SubmissionResult> SubmitOneAsync(ArticleToSubmit article, ArticleOrigin origin, CancellationToken ct)
    {
        var url = article.Url?.Trim() ?? "";
        if (!UrlCanonicalizer.TryCanonicalize(url, out var canonical))
        {
            return new SubmissionResult(url, SubmissionStatus.Rejected, Reason: "Not an absolute http(s) URL");
        }

        var articleId = ArticleIdentity.FromCanonicalUrl(canonical);
        if (!SourceCatalog.TryResolve(canonical, out var source))
        {
            return new SubmissionResult(url, SubmissionStatus.Rejected, articleId, "Unknown source");
        }
        if (!source.Enabled)
        {
            return new SubmissionResult(url, SubmissionStatus.Rejected, articleId, $"Source '{source.Id}' is not enabled yet");
        }

        var now = clock.GetUtcNow();
        var record = new LedgerRecord(
            articleId, source.Id, origin, url,
            new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, now) });

        // Known article (also a repeat within the same batch): nothing changes.
        if (!await ledger.TryCreateAsync(record, ct))
        {
            return new SubmissionResult(url, SubmissionStatus.Duplicate, articleId);
        }

        // The original URL is what Fetch downloads; the canonical one is for identity only.
        await queue.SendAsync(
            QueueNames.ArticleSubmitted,
            new ArticleSubmitted(articleId, url, canonical, source.Id, origin, now, article.Title, article.PublishedAt),
            ct);
        return new SubmissionResult(url, SubmissionStatus.Accepted, articleId);
    }
}
