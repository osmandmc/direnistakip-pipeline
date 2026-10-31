using Pipeline.Contracts;
using Pipeline.Core.Sources;
using Pipeline.Core.Submission;

namespace Pipeline.Core.Collecting;

/// <summary>What one run found in a source's feed. Found: items in the source's section; OutsideSection: those left out (B18).</summary>
public sealed record FeedCollectionResult(string SourceId, int Found, int Accepted, int Duplicate, int Rejected, int OutsideSection = 0);

/// <summary>
/// Reads a source's feed, or its section page when it has no feed (S1), and submits its articles (origin Collector).
/// Articles already in the ledger come back as duplicates and change nothing, so overlapping runs are harmless. A feed
/// that cannot be read throws; the next run tries again.
/// </summary>
public sealed class FeedCollector(HttpClient http, ArticleSubmission submission)
{
    /// <exception cref="HttpRequestException">The feed or section page could not be downloaded.</exception>
    /// <exception cref="FormatException">The download is not an RSS feed, or the section page has no article links.</exception>
    public async Task<FeedCollectionResult> CollectAsync(Source source, CancellationToken ct = default)
    {
        IReadOnlyList<ArticleToSubmit> articles;
        var outside = 0;
        if (source.FeedUrl is { } feedUrl)
        {
            using var response = await GetAsync(feedUrl, "Feed", ct);
            await using var feed = await response.Content.ReadAsStreamAsync(ct);
            (articles, outside) = FeedReader.ReadAll(feed, source.FeedHasFullText, source.FeedSection);
        }
        else if (source.SectionPage is { } section)
        {
            using var response = await GetAsync(section.Url, "Section page", ct);
            articles = SectionPageReader.Read(await response.Content.ReadAsStringAsync(ct), section);
        }
        else
        {
            throw new ArgumentException($"Source '{source.Id}' has no feed nor section page.", nameof(source));
        }

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
            results.Count(r => r.Status == SubmissionStatus.Rejected),
            outside);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string what, CancellationToken ct)
    {
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException($"{what} {url} answered HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        return response;
    }
}
