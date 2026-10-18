using Pipeline.Contracts;
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
        return new ArticleListResponse(
            page.Items.Select(r => new ArticleSummary(r.Id, r.SourceId, r.Origin, r.Url, r.Title, r.PublishedAt, r.Stages)).ToList(),
            page.Next);
    }

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
            text?.Body, text?.ParserVersion);
    }
}
