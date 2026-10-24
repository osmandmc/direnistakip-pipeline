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

    /// <summary>Sets the researchers' outcome without touching the stages.</summary>
    Task SetOutcomeAsync(string articleId, ArticleOutcome outcome, CancellationToken ct = default);

    /// <summary>Newest first by list date. <paramref name="continuation"/> is the previous page's <see cref="LedgerPage.Next"/>.</summary>
    Task<LedgerPage> QueryAsync(LedgerQuery query, int pageSize, string? continuation, CancellationToken ct = default);

    /// <summary>How many records match (same filters as <see cref="QueryAsync"/>).</summary>
    Task<int> CountAsync(LedgerQuery query, CancellationToken ct = default);

    /// <summary>Every record written since the given moment (submitted, a stage recorded, an outcome set …). For metrics.</summary>
    Task<IReadOnlyList<LedgerRecord>> ChangedSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}

public enum ArticleStatusFilter
{
    /// <summary>Has text: ready to read.</summary>
    Parsed,

    /// <summary>Some stage failed.</summary>
    Failed,

    All,
}

public enum OutcomeFilter
{
    /// <summary>Not handled yet: the inbox.</summary>
    Open,

    /// <summary>Digested or dismissed.</summary>
    Handled,

    Any,
}

/// <summary>The Relevance stage's verdict (B10): the inbox shows the AI's rejections apart, never hidden.</summary>
public enum RelevanceFilter
{
    Any,

    /// <summary>Judged not relevant.</summary>
    NotRelevant,

    /// <summary>Everything else: relevant, uncertain, not assessed (yet), or the assessment failed.</summary>
    Other,

    /// <summary>Not assessed yet, or the assessment failed (skipped ones, pasted links, do not count).</summary>
    NotAssessed,
}

/// <summary>Whether the Extract stage left a draft (B12): the inbox's "Taslak hazır" tab.</summary>
public enum DraftFilter
{
    Any,

    /// <summary>A draft with a labour action in it.</summary>
    Ready,

    /// <summary>No such draft: not drafted (yet), failed, or the model found no action.</summary>
    None,
}

/// <param name="From">Inclusive, compared with the list date.</param>
/// <param name="Before">Exclusive.</param>
/// <param name="TitleContains">Text the title must contain, compared in folded form (<see cref="Articles.TitleSearch"/>).</param>
public sealed record LedgerQuery(
    string? SourceId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? Before = null,
    ArticleStatusFilter Status = ArticleStatusFilter.Parsed,
    OutcomeFilter Outcome = OutcomeFilter.Open,
    string? TitleContains = null,
    RelevanceFilter Relevance = RelevanceFilter.Any,
    DraftFilter Draft = DraftFilter.Any);

/// <summary>A page of records; <see cref="Next"/> is an opaque token for the following page, or null.</summary>
public sealed record LedgerPage(IReadOnlyList<LedgerRecord> Items, string? Next);
