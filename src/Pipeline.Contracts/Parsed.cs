namespace Pipeline.Contracts;

/// <summary>
/// Output of the Parse stage (D-003): one record per parse, never overwritten.
/// A newer parser version creates a new record next to the old one.
/// </summary>
public sealed record ParsedArticle(
    string Id,
    string ArticleId,
    string SourceId,
    string ParserVersion,
    DateTimeOffset ParsedAt,
    string RawHtmlPath,
    string? Title,
    DateTimeOffset? PublishedAt,
    string Body);
