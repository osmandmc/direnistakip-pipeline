using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Relevance;

/// <summary>What happened to one article.</summary>
public enum AssessOutcome
{
    Assessed,

    /// <summary>A link pasted by a researcher: chosen on purpose, not assessed.</summary>
    Skipped,

    /// <summary>The model asked us to slow down; tried again later.</summary>
    Deferred,

    /// <summary>The model's answer could not be read (recorded as a failed stage).</summary>
    Failed,

    /// <summary>No key configured, or no text yet: nothing done.</summary>
    NotRun,
}

/// <summary>
/// The Relevance stage (D-007, B9): asks the model whether a parsed article tells of a labour action, saves the answer
/// as its own record and puts the verdict and reason in the ledger, where the app reads them. Articles researchers
/// pasted are skipped; without a model key nothing happens, so articles flow as before. Relevant articles are not
/// passed on yet: there is no Extract stage to take them (article-relevant stays unused until then).
/// </summary>
public sealed class RelevanceAssessment(
    ILedgerStore ledger,
    IParsedStore parsed,
    IRelevanceStore relevance,
    ILanguageModel model,
    IQueueSender queue,
    TimeProvider clock)
{
    public const string ManualReason = "Chosen by a researcher (pasted link)";

    /// <summary>Added to a rate limit's wait, so deferred articles do not all come back in the same second.</summary>
    public static readonly TimeSpan MaxJitter = TimeSpan.FromSeconds(60);

    /// <summary>How long an article waits after the model did not answer in time.</summary>
    public static readonly TimeSpan SlowAnswerRetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>The stage, for each message on article-parsed.</summary>
    public async Task<AssessOutcome> HandleAsync(ArticleParsed message, CancellationToken ct = default)
    {
        var record = await ledger.GetAsync(message.ArticleId, ct);
        // Unknown, delivered twice, or already handled by researchers (the queue had no reader before this stage, so it
        // starts with every article parsed until then).
        if (record is null || IsDone(record) || record.Outcome is not null)
        {
            return AssessOutcome.NotRun;
        }
        var (outcome, retryAfter) = await AssessAsync(record, ct);
        if (outcome == AssessOutcome.Deferred)
        {
            await queue.SendLaterAsync(QueueNames.ArticleParsed, message, retryAfter + Jitter(), ct);
        }
        return outcome;
    }

    /// <summary>Assessed or skipped already (a failed assessment may be tried again).</summary>
    public static bool IsDone(LedgerRecord record) =>
        record.Stages.TryGetValue(Stage.Relevance, out var done) && done.Status != StageStatus.Failed;

    public async Task<(AssessOutcome Outcome, TimeSpan RetryAfter)> AssessAsync(LedgerRecord record, CancellationToken ct = default)
    {
        if (record.Origin == ArticleOrigin.Manual)
        {
            await ledger.RecordStageAsync(record.Id, Stage.Relevance, new StageResult(StageStatus.Skipped, clock.GetUtcNow(), Reason: ManualReason), ct);
            return (AssessOutcome.Skipped, default);
        }
        if (!model.IsConfigured || await parsed.GetLatestAsync(record.Id, ct) is not { } article)
        {
            return (AssessOutcome.NotRun, default);
        }

        ModelAnswer answer;
        try
        {
            answer = await model.AnswerJsonAsync(RelevancePrompt.Build(article), ct);
        }
        catch (ModelRateLimitedException e)
        {
            return (AssessOutcome.Deferred, e.RetryAfter);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (AssessOutcome.Deferred, SlowAnswerRetryAfter); // the model did not answer in time: busy
        }
        catch (FormatException e)
        {
            await RecordFailureAsync(record.Id, RelevancePrompt.Version, e.Message, ct);
            return (AssessOutcome.Failed, default);
        }

        var version = $"{RelevancePrompt.Version} · {answer.Model}";
        RelevanceVerdict verdict;
        string? reason;
        try
        {
            (verdict, reason) = RelevancePrompt.ReadAnswer(answer.Text);
        }
        catch (FormatException e)
        {
            await RecordFailureAsync(record.Id, version, e.Message, ct);
            return (AssessOutcome.Failed, default);
        }

        var now = clock.GetUtcNow();
        await relevance.SaveAsync(new RelevanceRecord(Guid.NewGuid().ToString("N"), record.Id, article.Id, verdict, reason, version, now), ct);
        await ledger.RecordStageAsync(record.Id, Stage.Relevance, new StageResult(StageStatus.Succeeded, now, version, reason, verdict.ToString()), ct);
        return (AssessOutcome.Assessed, default);
    }

    private Task RecordFailureAsync(string articleId, string version, string reason, CancellationToken ct) =>
        ledger.RecordStageAsync(articleId, Stage.Relevance, new StageResult(StageStatus.Failed, clock.GetUtcNow(), version, reason), ct);

    private static TimeSpan Jitter() => TimeSpan.FromSeconds(Random.Shared.Next((int)MaxJitter.TotalSeconds));
}
