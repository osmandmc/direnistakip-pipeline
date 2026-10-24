using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

/// <summary>Is the article about a labour action the researchers record? (D-007)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelevanceVerdict>))]
public enum RelevanceVerdict
{
    Relevant,
    NotRelevant,

    /// <summary>Borderline or unclear; shown with the relevant ones (favour recall).</summary>
    Uncertain,
}

/// <summary>
/// One relevance assessment (D-003, D-007): one record per run, never overwritten. <see cref="Version"/> is the prompt
/// and the model ("relevance-v1 · gemini-…").
/// </summary>
public sealed record RelevanceRecord(
    string Id,
    string ArticleId,
    string ParsedId,
    RelevanceVerdict Verdict,
    string? Reason,
    string Version,
    DateTimeOffset AssessedAt);
