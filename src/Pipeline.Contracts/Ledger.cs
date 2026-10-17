using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<Stage>))]
public enum Stage
{
    Submitted,
    Fetched,
    Parsed,
    Relevance,
    Extracted,
    Validated,
}

[JsonConverter(typeof(JsonStringEnumConverter<StageStatus>))]
public enum StageStatus
{
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>Outcome of one stage for one article. Version is the parser/prompt/model/schema tag (D-003).</summary>
public sealed record StageResult(
    StageStatus Status,
    DateTimeOffset At,
    string? Version = null,
    string? Reason = null);

/// <summary>One record per article (D-015). Id is the ArticleId and the partition key (D-020).</summary>
public sealed record LedgerRecord(
    string Id,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    IReadOnlyDictionary<Stage, StageResult> Stages,
    ArticleOutcome? Outcome = null);
