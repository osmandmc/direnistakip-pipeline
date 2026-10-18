using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Submission;

/// <summary>
/// Takes articles into the pipeline (D-002): one ledger record per ArticleId. Articles with content
/// enter already parsed; the rest are queued for Fetch. Every entry point (manual, CSV, later the
/// RSS timer) goes through here.
/// </summary>
public sealed class ArticleSubmission(ILedgerStore ledger, IParsedStore parsed, IQueueSender queue, TimeProvider clock)
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

        if (!string.IsNullOrWhiteSpace(article.Content))
        {
            await TakeProvidedContentAsync(articleId, source.Id, origin, article, now, ct);
            return new SubmissionResult(url, SubmissionStatus.Accepted, articleId);
        }

        // The original URL is what Fetch downloads; the canonical one is for identity only.
        await queue.SendAsync(
            QueueNames.ArticleSubmitted,
            new ArticleSubmitted(articleId, url, canonical, source.Id, origin, now, article.Title, article.PublishedAt),
            ct);
        return new SubmissionResult(url, SubmissionStatus.Accepted, articleId);
    }

    /// <summary>
    /// Content came with the submission: save it as the parsed record and skip Fetch and Parse.
    /// If this stops halfway, the ledger still shows only "Submitted", which is visible and safe.
    /// </summary>
    private async Task TakeProvidedContentAsync(
        string articleId, string sourceId, ArticleOrigin origin, ArticleToSubmit article, DateTimeOffset now, CancellationToken ct)
    {
        var version = ProvidedVersion(origin);
        var record = new ParsedArticle(
            Guid.NewGuid().ToString("N"), articleId, sourceId, version, now,
            RawHtmlPath: null, article.Title?.Trim(), article.PublishedAt, article.Content!.Trim());

        await parsed.SaveAsync(record, ct);
        await ledger.RecordStageAsync(articleId, Stage.Parsed, new StageResult(StageStatus.Succeeded, now, version), ct);
        await queue.SendAsync(QueueNames.ArticleParsed, new ArticleParsed(articleId, sourceId, record.Id, version, now), ct);
    }

    /// <summary>Parser version for text that was provided rather than parsed, e.g. "provided:csv".</summary>
    public static string ProvidedVersion(ArticleOrigin origin) => $"provided:{origin.ToString().ToLowerInvariant()}";
}
