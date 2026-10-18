using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

/// <summary>Body of <c>POST /articles</c>: the way into the pipeline for manual and CSV submissions.</summary>
public sealed record SubmitArticlesRequest(
    IReadOnlyList<ArticleToSubmit> Articles,
    ArticleOrigin Origin = ArticleOrigin.Manual);

/// <summary>
/// An article to take in. With <see cref="Content"/> (e.g. Evrensel's CSV: Url, Title, Content, Date) it enters
/// already parsed and skips Fetch and Parse. Without it, the pipeline fetches and parses the page itself.
/// Dates without a time zone should be sent as Turkey time (+03:00).
/// </summary>
public sealed record ArticleToSubmit(
    string Url,
    string? Title = null,
    DateTimeOffset? PublishedAt = null,
    string? Content = null);

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
