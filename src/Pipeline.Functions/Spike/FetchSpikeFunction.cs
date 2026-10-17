using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Pipeline.Core.Identity;
using Pipeline.Functions.Http;

namespace Pipeline.Functions.Spike;

/// <summary>
/// TEMPORARY (T4 fetch spike). Answers: can each source be read from Azure, is the article
/// in the HTML without JavaScript, and what do article URLs look like? Saves raw HTML to
/// blob storage for the Parse task (T8). Remove once Fetch (T7) and Parse (T8) exist.
/// </summary>
public sealed partial class FetchSpikeFunction(
    IHttpClientFactory httpFactory,
    BlobServiceClient blobs,
    ILogger<FetchSpikeFunction> logger)
{
    private const string Container = "raw-html";
    private static readonly TimeSpan DelayBetweenRequests = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record SpikeSource(string DiscoveryUrl, Regex ArticleLink);

    // Discovery pages found from this repo's robots.txt / sitemap check.
    private static readonly Dictionary<string, SpikeSource> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["evrensel"] = new("https://www.evrensel.net/sitemap-news.xml", EvrenselLink()),
        ["birgun"] = new("https://www.birgun.net/sitemap/haberler", BirgunLink()),
        ["sendika"] = new("https://sendika.org/", SendikaLink()),
        ["kizilbayrak"] = new("http://www.kizilbayrak.net/rss", KizilbayrakLink()),
    };

    [Function("FetchSpike")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "spike/fetch")] HttpRequest req,
        CancellationToken ct)
    {
        var sourceId = req.Query["source"].ToString();
        if (!Sources.TryGetValue(sourceId, out var source))
        {
            return new BadRequestObjectResult(new { error = $"source must be one of: {string.Join(", ", Sources.Keys)}" });
        }
        var count = int.TryParse(req.Query["count"], out var c) ? Math.Clamp(c, 1, 30) : 20;

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var http = httpFactory.CreateClient(NewsHttp.ClientName);
        var container = blobs.GetBlobContainerClient(Container);

        var discovery = await FetchAsync(http, source.DiscoveryUrl, ct);
        var links = discovery.Body is null
            ? []
            : source.ArticleLink.Matches(discovery.Body)
                .Select(m => new Uri(new Uri(discovery.FinalUrl), m.Value).AbsoluteUri)
                .Distinct()
                .Take(count)
                .ToList();

        var articles = new List<ArticleResult>();
        foreach (var url in links)
        {
            await Task.Delay(DelayBetweenRequests, ct);
            var fetched = await FetchAsync(http, url, ct);
            var articleId = ArticleIdentity.FromUrl(url);
            if (fetched.Body is not null)
            {
                await container.GetBlobClient($"spike/{runId}/{sourceId}/{articleId}.html").UploadAsync(
                    BinaryData.FromString(fetched.Body),
                    new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = "text/html; charset=utf-8" },
                        Metadata = new Dictionary<string, string> { ["url"] = Uri.EscapeDataString(url) },
                    },
                    ct);
            }
            articles.Add(new ArticleResult(
                url, UrlCanonicalizer.Canonicalize(url), articleId,
                fetched.Status, fetched.FinalUrl, fetched.ElapsedMs, fetched.Bytes, fetched.Error,
                fetched.Body is null ? null : Inspect(fetched.Body)));
        }

        var report = new
        {
            runId,
            source = sourceId,
            summary = new
            {
                linksFound = links.Count,
                statuses = articles.GroupBy(a => a.Status?.ToString() ?? "error").ToDictionary(g => g.Key, g => g.Count()),
                challenged = articles.Count(a => a.Signals?.Challenge == true),
                withArticleTag = articles.Count(a => a.Signals?.ArticleTag == true),
                withPublishedTime = articles.Count(a => a.Signals?.PublishedTime == true),
                medianTextChars = Median(articles.Where(a => a.Signals is not null).Select(a => a.Signals!.VisibleTextChars)),
                canonicalHosts = articles.Select(a => new Uri(a.Canonical).Host).Distinct(),
            },
            discovery = new { source.DiscoveryUrl, discovery.Status, discovery.FinalUrl, discovery.Error },
            articles,
        };
        await container.GetBlobClient($"spike/{runId}/{sourceId}/report.json")
            .UploadAsync(BinaryData.FromObjectAsJson(report, Json), overwrite: true, ct);

        return new OkObjectResult(report);
    }

    private sealed record FetchResult(int? Status, string FinalUrl, long ElapsedMs, int Bytes, string? Body, string? Error);

    private sealed record Signals(
        bool Challenge, bool ArticleTag, bool OgTitle, bool JsonLdArticle, bool PublishedTime, int VisibleTextChars);

    private sealed record ArticleResult(
        string Url, string Canonical, string ArticleId,
        int? Status, string FinalUrl, long ElapsedMs, int Bytes, string? Error, Signals? Signals);

    private async Task<FetchResult> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;
            return new FetchResult((int)response.StatusCode, finalUrl, sw.ElapsedMilliseconds, body.Length, body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Spike fetch failed for {Url}", url);
            return new FetchResult(null, url, sw.ElapsedMilliseconds, 0, null, ex.GetBaseException().Message);
        }
    }

    /// <summary>Cheap signals: blocked by a bot challenge? Is the article in the HTML itself?</summary>
    private static Signals Inspect(string html)
    {
        var text = Whitespace().Replace(Tag().Replace(ScriptOrStyle().Replace(html, " "), " "), " ").Trim();
        return new Signals(
            Challenge: html.Contains("cf-chl", StringComparison.OrdinalIgnoreCase)
                || html.Contains("Just a moment...", StringComparison.OrdinalIgnoreCase),
            ArticleTag: html.Contains("<article", StringComparison.OrdinalIgnoreCase),
            OgTitle: html.Contains("og:title", StringComparison.OrdinalIgnoreCase),
            JsonLdArticle: html.Contains("\"NewsArticle\"", StringComparison.Ordinal)
                || html.Contains("\"Article\"", StringComparison.Ordinal),
            PublishedTime: html.Contains("article:published_time", StringComparison.OrdinalIgnoreCase)
                || html.Contains("datePublished", StringComparison.Ordinal),
            VisibleTextChars: text.Length);
    }

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    [GeneratedRegex(@"https://www\.evrensel\.net/haber/\d+/[^<""'\s]+")]
    private static partial Regex EvrenselLink();

    [GeneratedRegex(@"https://www\.birgun\.net/haber/[^<""'\s]+")]
    private static partial Regex BirgunLink();

    [GeneratedRegex(@"/20\d{2}/\d{2}/[a-z0-9-]+-\d+")]
    private static partial Regex SendikaLink();

    [GeneratedRegex(@"https?://(?:www\.)?kizilbayrak\d*\.net/ana-sayfa/[^<""'\s]+")]
    private static partial Regex KizilbayrakLink();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
