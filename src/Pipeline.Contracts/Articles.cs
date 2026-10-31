namespace Pipeline.Contracts;

/// <summary>One row of <c>GET /articles</c>: what the inbox list shows. No body text.</summary>
public sealed record ArticleSummary(
    string ArticleId,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<Stage, StageResult> Stages,
    ArticleOutcome? Outcome = null);

/// <summary>
/// A page of <c>GET /articles</c>. Pass <see cref="Next"/> back as <c>next</c> to load more;
/// null means there is nothing more.
/// </summary>
public sealed record ArticleListResponse(IReadOnlyList<ArticleSummary> Items, string? Next);

/// <summary><c>GET /articles/{id}</c>: one article with its latest text (null until it is parsed) and picture address.</summary>
public sealed record ArticleDetail(
    string ArticleId,
    string SourceId,
    ArticleOrigin Origin,
    string Url,
    string? Title,
    DateTimeOffset? PublishedAt,
    IReadOnlyDictionary<Stage, StageResult> Stages,
    string? Body,
    string? ParserVersion,
    ArticleOutcome? Outcome = null,
    string? ImageUrl = null);

/// <summary>
/// <c>POST /articles/reparse</c>: articles parsed again from their stored pages. More: there were more than one run
/// handles; call again (articles that parsed now no longer match "failed").
/// </summary>
public sealed record ReparseResponse(int Examined, int Parsed, int StillFailed, int NoStoredPage, bool More);

/// <summary>
/// <c>POST /articles/assess</c> (B9): open articles queued for the Relevance stage, spaced <c>SpacingSeconds</c> apart.
/// More: there were more than one call queues; call again once these are done (about <c>Queued × SpacingSeconds</c>).
/// ModelConfigured false: no key, nothing queued.
/// </summary>
public sealed record AssessResponse(int Queued, int SpacingSeconds, bool More, bool ModelConfigured);

/// <summary>
/// <c>GET /prompts</c> (B13): the prompts the AI stages run now, and how many open articles were judged by an older
/// relevance prompt (they can be judged again: <c>POST /articles/assess?again=true</c>).
/// </summary>
/// ListsUpdatedAt: the date of the list values the extraction prompt offers (B16); null: the snapshot of 2026-10-21.
public sealed record PromptsResponse(IReadOnlyList<PromptInfo> Prompts, int OpenWithOlderVerdict, DateTimeOffset? ListsUpdatedAt = null);

/// <param name="Name">"relevance" or "extraction".</param>
/// <param name="Version">The version the stage runs now: the active edited one (B17), or the built-in one.</param>
/// <param name="Text">The prompt with its placeholders ({{TITLE}}, {{TEXT}}, {{LISTS}} …).</param>
/// <param name="Builtin">The version in the repository (docs/extraction), used when no edited version is active.</param>
/// <param name="Versions">Versions saved in the app (B17), newest first.</param>
public sealed record PromptInfo(string Name, string Version, string Model, string Text, string? Builtin = null,
    IReadOnlyList<PromptVersionInfo>? Versions = null);

/// <summary>A prompt version saved in the app (B17). Active: the stage runs it now.</summary>
public sealed record PromptVersionInfo(string Version, string Text, string? Note, string? CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt, bool Active);

/// <summary>A prompt version as stored (B17): "relevance-v3.1-e1" is the first edit made on top of built-in relevance-v3.1.</summary>
public sealed record PromptVersionRecord(string Name, string Version, string Text, string? Note, string? CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt = null, string? ActivatedBy = null);

/// <summary><c>POST /api/prompts/{name}/versions</c>: a new version, not active yet.</summary>
public sealed record PromptVersionRequest(string Text, string? Note, string? By);

/// <summary><c>POST /api/prompts/{name}/active</c>: Version "builtin" goes back to the repository's prompt.</summary>
public sealed record PromptActivateRequest(string Version, string? By);

/// <summary><c>POST /api/prompts/{name}/versions/{version}/try</c>: run the version on these articles (at most 10); nothing is saved.</summary>
public sealed record PromptTryRequest(IReadOnlyList<string> ArticleIds);

/// <summary>
/// One article tried. Answer: the verdict ("Relevant", "NotRelevant", "Uncertain") or the extraction JSON; Reason: the
/// verdict's reason or the draft's summary. Current…: what the running prompt concluded (ledger, latest draft). Error:
/// why it was not tried (no text, the model's quota, an unreadable answer).
/// </summary>
public sealed record PromptTryResult(string ArticleId, string? Title, string? Answer, string? Reason, string? Error,
    string? CurrentAnswer, string? CurrentReason, string? CurrentVersion);

public sealed record PromptTryResponse(string Version, string? Model, IReadOnlyList<PromptTryResult> Results);
