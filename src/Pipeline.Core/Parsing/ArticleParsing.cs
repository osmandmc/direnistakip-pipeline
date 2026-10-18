using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Parsing;

/// <summary>
/// The Parse stage: reads the saved page, extracts title, date and body with the source's parser,
/// saves the parsed record and hands the article on (same message as articles submitted with content).
/// A parser that finds nothing usable is a permanent failure; storage errors throw and are retried.
/// </summary>
public sealed class ArticleParsing(
    IEnumerable<IArticleParser> parsers,
    ILedgerStore ledger,
    IRawHtmlStore rawHtml,
    IParsedStore parsed,
    IQueueSender queue,
    TimeProvider clock)
{
    private readonly Dictionary<string, IArticleParser> _parsers = parsers.ToDictionary(p => p.SourceId);

    public async Task ParseAsync(ArticleFetched article, CancellationToken ct = default)
    {
        var record = await ledger.GetAsync(article.ArticleId, ct);
        if (record is null)
        {
            return;
        }
        if (record.Stages.TryGetValue(Stage.Parsed, out var done) && done.Status == StageStatus.Succeeded)
        {
            return; // Delivered twice, or the article came with content.
        }

        if (!_parsers.TryGetValue(article.SourceId, out var parser))
        {
            await RecordFailureAsync(article.ArticleId, null, $"No parser for source '{article.SourceId}'", ct);
            return;
        }

        var html = await rawHtml.ReadAsync(article.RawHtmlPath, ct);
        ParseResult result;
        try
        {
            result = parser.Parse(html);
        }
        catch (Exception ex)
        {
            result = ParseResult.Failed($"Parser error: {ex.Message}");
        }

        if (!result.Succeeded)
        {
            await RecordFailureAsync(article.ArticleId, parser.Version, result.Failure!, ct);
            return;
        }

        var now = clock.GetUtcNow();
        var parsedArticle = new ParsedArticle(
            Guid.NewGuid().ToString("N"), article.ArticleId, article.SourceId, parser.Version, now,
            article.RawHtmlPath, result.Title, result.PublishedAt, result.Body!);

        await parsed.SaveAsync(parsedArticle, ct);
        await ledger.SetSummaryAsync(article.ArticleId, result.Title, result.PublishedAt, ct);
        await ledger.RecordStageAsync(article.ArticleId, Stage.Parsed, new StageResult(StageStatus.Succeeded, now, parser.Version), ct);
        await queue.SendAsync(
            QueueNames.ArticleParsed,
            new ArticleParsed(article.ArticleId, article.SourceId, parsedArticle.Id, parser.Version, now),
            ct);
    }

    private Task RecordFailureAsync(string articleId, string? version, string reason, CancellationToken ct) =>
        ledger.RecordStageAsync(articleId, Stage.Parsed, new StageResult(StageStatus.Failed, clock.GetUtcNow(), version, reason), ct);
}
