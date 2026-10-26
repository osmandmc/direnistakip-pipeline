using Pipeline.Core.Prompts;
using Pipeline.Contracts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Extraction;

/// <summary>
/// The Extract stage (B12, phase 3): for articles judged relevant or uncertain, the model fills the case form with the
/// extraction prompt. The answer is kept as a draft record; the ledger gets "Draft" (a labour action found, with a
/// one-line summary for the inbox) or "NoEvent". The app checks a draft against its own lists before the researcher
/// sees it, as it does a pasted answer. Like the Relevance stage: articles researchers already handled are left alone,
/// nothing happens without a key, and a rate limit, a busy model or a slow answer sends the article back for later.
/// </summary>
public sealed class DraftExtraction(
    ILedgerStore ledger,
    IParsedStore parsed,
    IDraftStore drafts,
    ILanguageModel model,
    IQueueSender queue,
    TimeProvider clock,
    IListStore? listStore = null,
    ActivePrompts? prompts = null)
{
    public static readonly TimeSpan SlowAnswerRetryAfter = TimeSpan.FromMinutes(2);

    public async Task<AssessOutcome> HandleAsync(ArticleRelevant message, CancellationToken ct = default)
    {
        var record = await ledger.GetAsync(message.ArticleId, ct);
        if (record is null || IsDone(record) || record.Outcome is not null)
        {
            return AssessOutcome.NotRun;
        }
        if (!model.IsConfigured || await parsed.GetLatestAsync(record.Id, ct) is not { } article)
        {
            return AssessOutcome.NotRun;
        }

        ModelAnswer answer;
        ListsSnapshot? lists = null;
        var prompt = prompts is null ? ActivePrompts.Builtin(ActivePrompts.Extraction) : await prompts.GetAsync(ActivePrompts.Extraction, ct);
        try
        {
            lists = listStore is null ? null : await listStore.GetLatestAsync(ct);
            answer = await model.AnswerJsonAsync(ExtractionPrompt.Build(article, lists, prompt.Template), ct);
        }
        catch (ModelRateLimitedException e)
        {
            await SendBackAsync(message, e.RetryAfter, ct);
            return AssessOutcome.Deferred;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            await SendBackAsync(message, SlowAnswerRetryAfter, ct);
            return AssessOutcome.Deferred;
        }
        catch (FormatException e)
        {
            await RecordFailureAsync(record.Id, prompt.Version, e.Message, ct);
            return AssessOutcome.Failed;
        }

        var version = $"{prompt.Version} · {ExtractionPrompt.ListsLabel(lists)} · {answer.Model}";
        (string Json, bool Relevant, string? Summary) draft;
        try
        {
            draft = ExtractionPrompt.ReadAnswer(answer.Text);
        }
        catch (FormatException e)
        {
            await RecordFailureAsync(record.Id, version, e.Message, ct);
            return AssessOutcome.Failed;
        }

        var now = clock.GetUtcNow();
        await drafts.SaveAsync(new DraftRecord(Guid.NewGuid().ToString("N"), record.Id, article.Id, version, draft.Json, draft.Relevant, draft.Summary, now), ct);
        await ledger.RecordStageAsync(record.Id, Stage.Extracted, new StageResult(
            StageStatus.Succeeded, now, version, draft.Summary, draft.Relevant ? ExtractionAnswers.Draft : ExtractionAnswers.NoEvent), ct);
        return AssessOutcome.Assessed;
    }

    /// <summary>Drafted already (a failed run may be tried again).</summary>
    public static bool IsDone(LedgerRecord record) =>
        record.Stages.TryGetValue(Stage.Extracted, out var done) && done.Status != StageStatus.Failed;

    private Task SendBackAsync(ArticleRelevant message, TimeSpan after, CancellationToken ct) =>
        queue.SendLaterAsync(QueueNames.ArticleRelevant, message,
            after + TimeSpan.FromSeconds(Random.Shared.Next((int)RelevanceAssessment.MaxJitter.TotalSeconds)), ct);

    private Task RecordFailureAsync(string articleId, string version, string reason, CancellationToken ct) =>
        ledger.RecordStageAsync(articleId, Stage.Extracted, new StageResult(StageStatus.Failed, clock.GetUtcNow(), version, reason), ct);
}
