namespace Pipeline.Contracts;

/// <summary>
/// <c>GET /metrics?days=14</c>: what came in and what happened to it, per source and per day (Turkey time).
/// Every event counts on its own day: an article received on Monday and read on Tuesday adds to both days.
/// </summary>
public sealed record MetricsResponse(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<SourceMetrics> Sources,
    IReadOnlyList<DailyMetrics> Days,
    IReadOnlyList<FailureSummary> RecentFailures,
    AiMetrics? Ai = null);

/// <param name="LastReceivedAt">Latest article received in the period; null if none (a silent source).</param>
/// <param name="Open">Read and waiting in the inbox now, whatever the period.</param>
/// <param name="OpenWithDraft">Of those: with an AI draft (B12).</param>
/// <param name="OpenNotRelevant">Of those: judged not relevant by the AI (B9).</param>
/// <param name="OpenNotAssessed">Of those: not assessed by the AI yet, or the assessment failed (pasted links excepted).</param>
public sealed record SourceMetrics(
    string SourceId,
    DateTimeOffset? LastReceivedAt,
    ArticleOrigin? LastReceivedOrigin,
    int Received,
    int Failed,
    int Open,
    int OpenWithDraft = 0,
    int OpenNotRelevant = 0,
    int OpenNotAssessed = 0);

/// <summary>
/// One day of one source; days without activity are included with zeros. Assessed…: the AI's verdicts given that day
/// (B9); Drafted: drafts with a labour action made that day (B12). Now: where the articles received that day stand now
/// (B14); its counts add up to Received.
/// </summary>
public sealed record DailyMetrics(
    DateOnly Day, string SourceId, int Received, int Parsed, int Failed, int Digested, int Dismissed,
    int AssessedRelevant = 0, int AssessedUncertain = 0, int AssessedNotRelevant = 0, int Drafted = 0,
    ArticleStates? Now = null);

/// <summary>
/// Articles by where they stand now (B14). Linked / Dismissed: the researchers' decision. Open ones: WithDraft (an AI
/// draft, not rejected by the AI), NotRelevant (rejected by the AI), ToReview (the rest). Failed: download or reading
/// failed (a failed AI step does not count). Processing: received, text not read yet.
/// </summary>
public sealed record ArticleStates(int Linked, int Dismissed, int WithDraft, int ToReview, int NotRelevant, int Failed, int Processing);

/// <summary>
/// The AI stages (B11). Enabled: a model key is configured. Agreement: for articles researchers decided in the period
/// and the AI had judged, what the AI said against what the researchers did; NotRelevant with Digested &gt; 0 means the
/// AI rejected articles that became cases.
/// </summary>
public sealed record AiMetrics(bool Enabled, IReadOnlyList<VerdictAgreement> Agreement);

public sealed record VerdictAgreement(RelevanceVerdict Verdict, int Digested, int Dismissed);

/// <summary>A stage that failed in the period, newest first.</summary>
public sealed record FailureSummary(string ArticleId, string SourceId, string Url, string? Title, Stage Stage, DateTimeOffset At, string? Reason);
