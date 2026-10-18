using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

/// <summary>Body of <c>POST /articles</c>: the way into the pipeline for manual and CSV submissions.</summary>
public sealed record SubmitArticlesRequest(
    IReadOnlyList<ArticleToSubmit> Articles,
    ArticleOrigin Origin = ArticleOrigin.Manual);

/// <summary>Title and date are optional; the CSV upload may know them before the page is parsed.</summary>
public sealed record ArticleToSubmit(
    string Url,
    string? Title = null,
    DateTimeOffset? PublishedAt = null);

[JsonConverter(typeof(JsonStringEnumConverter<SubmissionStatus>))]
public enum SubmissionStatus
{
    /// <summary>New article: ledger record created and queued for fetching.</summary>
    Accepted,

    /// <summary>Already known (same ArticleId); nothing changed.</summary>
    Duplicate,

    /// <summary>Not taken in; see the reason.</summary>
    Rejected,
}

public sealed record SubmissionResult(
    string Url,
    SubmissionStatus Status,
    string? ArticleId = null,
    string? Reason = null);

public sealed record SubmitArticlesResponse(IReadOnlyList<SubmissionResult> Results);
