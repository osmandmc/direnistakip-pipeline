namespace Pipeline.Contracts;

/// <summary>
/// An article's text (D-003): one record per parse, never overwritten. Made by our parser from raw HTML,
/// or provided with the submission (ParserVersion "provided:csv", no raw HTML).
/// </summary>
public sealed record ParsedArticle(
    string Id,
    string ArticleId,
    string SourceId,
    string ParserVersion,
    DateTimeOffset ParsedAt,
    string? RawHtmlPath,
    string? Title,
    DateTimeOffset? PublishedAt,
    string Body);
