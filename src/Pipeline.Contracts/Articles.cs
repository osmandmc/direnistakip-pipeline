namespace Pipeline.Contracts;

/// <summary>One row of <c>GET /articles</c>: what the inbox list shows. No body text.</summary>
public sealed record ArticleSummary(
    string ArticleId,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<Stage, StageResult> Stages);

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
    string? ParserVersion);
