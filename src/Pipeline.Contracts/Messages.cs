using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ArticleOrigin>))]
public enum ArticleOrigin
{
    Csv,
    Collector,
    Manual,
}

/// <summary>
/// Emitted by every entry point (D-002). The pipeline's single way in.
/// </summary>
public sealed record ArticleSubmitted(
    string ArticleId,
    string Url,
    string CanonicalUrl,
    string SourceId,
    ArticleOrigin Origin,
    DateTimeOffset SubmittedAt,
    string? Title = null,
    DateTimeOffset? PublishedAt = null);

/// <summary>Sent by Fetch when the page is saved; Parse reads it from <see cref="RawHtmlPath"/>.</summary>
public sealed record ArticleFetched(
    string ArticleId,
    string SourceId,
    string RawHtmlPath,
    string FinalUrl,
    DateTimeOffset FetchedAt);

/// <summary>
/// Sent when an article's text is available, by Parse or by a submission with content. Reassess: sent again on request
/// (B13) so an article judged by an older relevance prompt is judged with the current one.
/// </summary>
public sealed record ArticleParsed(
    string ArticleId,
    string SourceId,
    string ParsedId,
    string ParserVersion,
    DateTimeOffset ParsedAt,
    bool Reassess = false);
