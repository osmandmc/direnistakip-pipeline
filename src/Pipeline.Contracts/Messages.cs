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
