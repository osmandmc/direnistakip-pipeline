namespace Pipeline.Contracts;

/// <summary>Sent by the Relevance stage for articles judged relevant or uncertain: the Extract stage drafts them (B12).</summary>
public sealed record ArticleRelevant(string ArticleId, string SourceId, RelevanceVerdict Verdict, DateTimeOffset AssessedAt);

/// <summary>
/// One AI draft of the case form (D-003, B12): one record per run, never overwritten. <see cref="Answer"/> is the model's
/// JSON in the shape of docs/extraction/prompt.md; the app checks it against its lists, as a pasted answer (app R19).
/// Relevant: the answer's own verdict (false = no labour action found). Version: prompt · model.
/// </summary>
public sealed record DraftRecord(
    string Id,
    string ArticleId,
    string ParsedId,
    string Version,
    string Answer,
    bool Relevant,
    string? Summary,
    DateTimeOffset CreatedAt);

/// <summary><c>GET /articles/{id}/draft</c>: the article's latest draft.</summary>
public sealed record DraftResponse(string ArticleId, string Version, string Answer, bool Relevant, string? Summary, DateTimeOffset CreatedAt);

/// <summary>
/// <c>POST /articles/draft</c> (B12): open articles judged relevant or uncertain but without a draft, queued for the
/// Extract stage <c>SpacingSeconds</c> apart. More: call again once these are done.
/// </summary>
public sealed record DraftQueueResponse(int Queued, int SpacingSeconds, bool More, bool ModelConfigured);
