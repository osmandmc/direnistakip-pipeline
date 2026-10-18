using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>One status record per article (D-015).</summary>
public interface ILedgerStore
{
    /// <summary>Creates the record unless one with the same id exists. Returns false if it already existed.</summary>
    Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default);

    Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default);

    /// <summary>Sets one stage's result without touching the others, so stages can update concurrently.</summary>
    Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default);

    /// <summary>Copies title and publication date onto the record once known (they also become the list date).</summary>
    Task SetSummaryAsync(string articleId, string? title, DateTimeOffset? publishedAt, CancellationToken ct = default);

    /// <summary>Newest first by list date. <paramref name="continuation"/> is the previous page's <see cref="LedgerPage.Next"/>.</summary>
    Task<LedgerPage> QueryAsync(LedgerQuery query, int pageSize, string? continuation, CancellationToken ct = default);
}

public enum ArticleStatusFilter
{
    /// <summary>Has text: ready to read.</summary>
    Parsed,

    /// <summary>Some stage failed.</summary>
    Failed,

    All,
}

/// <param name="From">Inclusive, compared with the list date.</param>
/// <param name="Before">Exclusive.</param>
public sealed record LedgerQuery(
    string? SourceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Before = null,
    ArticleStatusFilter Status = ArticleStatusFilter.Parsed);

/// <summary>A page of records; <see cref="Next"/> is an opaque token for the following page, or null.</summary>
public sealed record LedgerPage(IReadOnlyList<LedgerRecord> Items, string? Next);
