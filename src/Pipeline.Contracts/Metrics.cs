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
    IReadOnlyList<FailureSummary> RecentFailures);

/// <param name="LastReceivedAt">Latest article received in the period; null if none (a silent source).</param>
/// <param name="Open">Read and waiting in the inbox now, whatever the period.</param>
public sealed record SourceMetrics(
    string SourceId,
    DateTimeOffset? LastReceivedAt,
    ArticleOrigin? LastReceivedOrigin,
    int Received,
    int Failed,
    int Open);

/// <summary>One day of one source; days without activity are included with zeros.</summary>
public sealed record DailyMetrics(DateOnly Day, string SourceId, int Received, int Parsed, int Failed, int Digested, int Dismissed);

/// <summary>A stage that failed in the period, newest first.</summary>
public sealed record FailureSummary(string ArticleId, string SourceId, string Url, string? Title, Stage Stage, DateTimeOffset At, string? Reason);
