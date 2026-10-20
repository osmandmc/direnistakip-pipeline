using Pipeline.Contracts;
using Pipeline.Core.Sources;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Collecting;

/// <summary>What one run found in a source's feed.</summary>
public sealed record FeedCollectionResult(string SourceId, int Found, int Accepted, int Duplicate, int Rejected);

/// <summary>
/// Reads a source's feed and submits its articles (origin Collector). Articles already in the ledger come back as
/// duplicates and change nothing, so overlapping runs are harmless. A feed that cannot be read throws; the next run tries again.
/// </summary>
public sealed class FeedCollector(HttpClient http, ArticleSubmission submission)
{
    /// <exception cref="HttpRequestException">The feed could not be downloaded.</exception>
    /// <exception cref="FormatException">The download is not an RSS feed.</exception>
    public async Task<FeedCollectionResult> CollectAsync(Source source, CancellationToken ct = default)
    {
        if (source.FeedUrl is null)
        {
            throw new ArgumentException($"Source '{source.Id}' has no feed.", nameof(source));
        }

        using var response = await http.GetAsync(source.FeedUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Feed {source.FeedUrl} answered HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        await using var feed = await response.Content.ReadAsStreamAsync(ct);
        var articles = FeedReader.Read(feed, source.FeedHasFullText);

        var results = new List<SubmissionResult>(articles.Count);
        foreach (var batch in articles.Chunk(ArticleSubmission.MaxBatchSize))
        {
            var submitted = await submission.SubmitAsync(new SubmitArticlesRequest(batch, ArticleOrigin.Collector), ct);
            results.AddRange(submitted.Results);
        }
        return new FeedCollectionResult(
            source.Id,
            articles.Count,
            results.Count(r => r.Status == SubmissionStatus.Accepted),
            results.Count(r => r.Status == SubmissionStatus.Duplicate),
            results.Count(r => r.Status == SubmissionStatus.Rejected));
    }
}
