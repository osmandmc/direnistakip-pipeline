using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Articles;

/// <summary>Builds the read API's responses (D-009): lists from the ledger only, details with the latest text.</summary>
public sealed class ArticleQueries(ILedgerStore ledger, IParsedStore parsed)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<ArticleListResponse> ListAsync(
        LedgerQuery query, int pageSize, string? next, CancellationToken ct = default)
    {
        var page = await ledger.QueryAsync(query, Math.Clamp(pageSize, 1, MaxPageSize), next, ct);
        return new ArticleListResponse(page.Items.Select(Summary).ToList(), page.Next);
    }

    /// <summary>
    /// The article with this link, whatever its status or outcome ("did this link already come in?").
    /// Null when the text is not a link.
    /// </summary>
    public async Task<ArticleListResponse?> FindByUrlAsync(string text, CancellationToken ct = default)
    {
        if (!UrlCanonicalizer.TryCanonicalize(text, out var canonical))
        {
            return null;
        }
        var record = await ledger.GetAsync(ArticleIdentity.FromCanonicalUrl(canonical), ct);
        return new ArticleListResponse(record is null ? [] : [Summary(record)], null);
    }

    private static ArticleSummary Summary(LedgerRecord r) =>
        new(r.Id, r.SourceId, r.Origin, r.Url, r.Title, r.PublishedAt, r.Stages, r.Outcome);

    public async Task<ArticleDetail?> GetAsync(string articleId, CancellationToken ct = default)
    {
        var record = await ledger.GetAsync(articleId, ct);
        if (record is null)
        {
            return null;
        }
        var text = await parsed.GetLatestAsync(articleId, ct);
        return new ArticleDetail(
            record.Id, record.SourceId, record.Origin, record.Url,
            record.Title ?? text?.Title, record.PublishedAt ?? text?.PublishedAt, record.Stages,
            text?.Body, text?.ParserVersion, record.Outcome, text?.ImageUrl);
    }
}
