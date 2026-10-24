namespace Pipeline.Contracts;

/// <summary>One row of <c>GET /articles</c>: what the inbox list shows. No body text.</summary>
public sealed record ArticleSummary(
    string ArticleId,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<Stage, StageResult> Stages,
    ArticleOutcome? Outcome = null);

/// <summary>
/// A page of <c>GET /articles</c>. Pass <see cref="Next"/> back as <c>next</c> to load more;
/// null means there is nothing more.
/// </summary>
public sealed record ArticleListResponse(IReadOnlyList<ArticleSummary> Items, string? Next);

/// <summary><c>GET /articles/{id}</c>: one article with its latest text (null until it is parsed).</summary>
public sealed record ArticleDetail(
    string ArticleId,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<Stage, StageResult> Stages,
    string? Body,
    string? ParserVersion,
    ArticleOutcome? Outcome = null);

/// <summary>
/// <c>POST /articles/reparse</c>: articles parsed again from their stored pages. More: there were more than one run
/// handles; call again (articles that parsed now no longer match "failed").
/// </summary>
public sealed record ReparseResponse(int Examined, int Parsed, int StillFailed, int NoStoredPage, bool More);

/// <summary>
/// <c>POST /articles/assess</c> (B9): open articles queued for the Relevance stage, spaced <c>SpacingSeconds</c> apart.
/// More: there were more than one call queues; call again once these are done (about <c>Queued × SpacingSeconds</c>).
/// ModelConfigured false: no key, nothing queued.
/// </summary>
public sealed record AssessResponse(int Queued, int SpacingSeconds, bool More, bool ModelConfigured);
