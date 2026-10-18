using System.Net;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Fetching;

/// <summary>
/// The Fetch stage: downloads an article's page, saves it as raw HTML (D-003) and hands it to Parse.
/// Permanent failures (404, not HTML, …) are recorded at once. Temporary ones (timeouts, 5xx, 429)
/// throw <see cref="TemporaryFetchException"/> so the queue retries; the last attempt records the failure.
/// </summary>
public sealed class ArticleFetcher(
    HttpClient http, ILedgerStore ledger, IRawHtmlStore rawHtml, IQueueSender queue, TimeProvider clock)
{
    /// <summary>Must match <c>maxDequeueCount</c> in host.json.</summary>
    public const int MaxAttempts = 5;

    public const int MaxPageChars = 5_000_000;

    /// <param name="attempt">1 for the first delivery of the queue message, 2 for the first retry, …</param>
    public async Task FetchAsync(ArticleSubmitted article, long attempt, CancellationToken ct = default)
    {
        var record = await ledger.GetAsync(article.ArticleId, ct);
        if (record is null)
        {
            return; // Not submitted through ArticleSubmission; nothing to record against.
        }
        if (record.Stages.TryGetValue(Stage.Fetched, out var fetched) && fetched.Status == StageStatus.Succeeded)
        {
            return; // Queues can deliver a message twice; the page is already saved.
        }

        var outcome = await DownloadAsync(article.Url, ct);
        var now = clock.GetUtcNow();

        if (outcome.Html is not null)
        {
            var path = await rawHtml.SaveAsync(article.SourceId, article.ArticleId, outcome.Html, now, ct);
            await ledger.RecordStageAsync(article.ArticleId, Stage.Fetched, new StageResult(StageStatus.Succeeded, now), ct);
            await queue.SendAsync(
                QueueNames.ArticleFetched,
                new ArticleFetched(article.ArticleId, article.SourceId, path, outcome.FinalUrl, now),
                ct);
            return;
        }

        if (outcome.Temporary && attempt < MaxAttempts)
        {
            throw new TemporaryFetchException($"{outcome.Reason} (attempt {attempt} of {MaxAttempts})");
        }

        var reason = outcome.Temporary ? $"Gave up after {MaxAttempts} attempts: {outcome.Reason}" : outcome.Reason;
        await ledger.RecordStageAsync(
            article.ArticleId, Stage.Fetched, new StageResult(StageStatus.Failed, now, Reason: reason), ct);
    }

    private sealed record Outcome(string? Html, string FinalUrl, string Reason, bool Temporary);

    private async Task<Outcome> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
            var status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                var temporary = status >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
                return new Outcome(null, finalUrl, $"HTTP {status}", temporary);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome(null, finalUrl, $"Not an HTML page ({mediaType ?? "no content type"})", Temporary: false);
            }
            if (response.Content.Headers.ContentLength > MaxPageChars)
            {
                return new Outcome(null, finalUrl, "Page too large", Temporary: false);
            }

            var html = await response.Content.ReadAsStringAsync(ct);
            return html.Length > MaxPageChars
                ? new Outcome(null, finalUrl, "Page too large", Temporary: false)
                : new Outcome(html, finalUrl, "", Temporary: false);
        }
        catch (HttpRequestException ex)
        {
            return new Outcome(null, url, $"Network error: {ex.GetBaseException().Message}", Temporary: true);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Outcome(null, url, "Timed out", Temporary: true);
        }
    }
}

/// <summary>Thrown for failures worth retrying; the queue redelivers the message after a short wait.</summary>
public sealed class TemporaryFetchException(string message) : Exception(message);
