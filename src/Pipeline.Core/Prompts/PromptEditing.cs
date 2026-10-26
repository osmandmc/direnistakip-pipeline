using System.Text.RegularExpressions;
using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Prompts;

/// <summary>
/// Prompt versions edited in the app (B17, app R28): save a new version, try it on a few articles without saving
/// anything, and activate it (or go back to the built-in prompt). A version is named after the built-in prompt it was
/// made on: "relevance-v3.1-e1", "relevance-v3.1-e2" …
/// </summary>
public sealed class PromptEditing(
    IPromptStore store,
    ActivePrompts active,
    ILedgerStore ledger,
    IParsedStore parsed,
    IDraftStore drafts,
    IListStore lists,
    ILanguageModel relevanceModel,
    ILanguageModel extractionModel,
    TimeProvider clock)
{
    public const string BuiltinVersion = "builtin";
    public const int MaxTextLength = 60_000;

    /// <summary>Articles one try may run: the free tier allows about 15 requests a minute.</summary>
    public const int MaxTry = 10;

    /// <summary>Error codes of a tried article (the app words them).</summary>
    public const string NoText = "NoText";
    public const string RateLimited = "RateLimited";
    public const string NotConfigured = "NotConfigured";
    public const string Unreadable = "Unreadable";

    private static readonly Dictionary<string, string[]> Placeholders = new()
    {
        [ActivePrompts.Relevance] = ["{{TITLE}}", "{{TEXT}}"],
        [ActivePrompts.Extraction] = ["{{LISTS}}", "{{TITLE}}", "{{TEXT}}"],
    };

    public static bool IsKnown(string name) => Placeholders.ContainsKey(name ?? "");

    /// <summary>The placeholders a version of the prompt must keep.</summary>
    public static IReadOnlyList<string> Required(string name) => Placeholders[name];

    /// <exception cref="ArgumentException">Unknown prompt, empty or too long text, or a placeholder missing.</exception>
    public async Task<PromptVersionRecord> CreateAsync(string name, PromptVersionRequest request, CancellationToken ct = default)
    {
        if (!IsKnown(name))
            throw new ArgumentException($"Unknown prompt '{name}': relevance or extraction.");
        var text = request.Text?.Replace("\r\n", "\n").Trim() ?? "";
        if (text.Length == 0 || text.Length > MaxTextLength)
            throw new ArgumentException($"The text is empty or longer than {MaxTextLength} characters.");
        var missing = Placeholders[name].Where(p => !text.Contains(p, StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException("The text must keep these placeholders: " + string.Join(", ", missing));

        var builtin = ActivePrompts.Builtin(name).Version;
        var pattern = new Regex("^" + Regex.Escape(builtin) + @"-e(\d+)$");
        var next = (await store.ListAsync(name, ct))
            .Select(v => pattern.Match(v.Version))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max() + 1;
        var version = new PromptVersionRecord(name, $"{builtin}-e{next}", text, Trim(request.Note, 500), Trim(request.By, 100), clock.GetUtcNow());
        await store.SaveAsync(version, ct);
        return version;
    }

    /// <summary>The version now active, or null when the version does not exist.</summary>
    /// <exception cref="ArgumentException">Unknown prompt.</exception>
    public async Task<ActivePrompt?> ActivateAsync(string name, string version, string? by, CancellationToken ct = default)
    {
        if (!IsKnown(name))
            throw new ArgumentException($"Unknown prompt '{name}': relevance or extraction.");
        if (version == BuiltinVersion)
        {
            await store.SetActiveAsync(name, null, ct);
            active.Forget(name);
            return ActivePrompts.Builtin(name);
        }
        if (await store.GetAsync(name, version, ct) is not { } record)
            return null;
        await store.SetActiveAsync(name, record with { ActivatedAt = clock.GetUtcNow(), ActivatedBy = Trim(by, 100) }, ct);
        active.Forget(name);
        return new ActivePrompt(record.Version, record.Text);
    }

    /// <summary>Runs the version (or "builtin") on the articles, at most <see cref="MaxTry"/>; null when the version does not exist.</summary>
    public async Task<PromptTryResponse?> TryAsync(string name, string version, IReadOnlyList<string> articleIds, CancellationToken ct = default)
    {
        if (!IsKnown(name))
            throw new ArgumentException($"Unknown prompt '{name}': relevance or extraction.");
        var prompt = version == BuiltinVersion ? ActivePrompts.Builtin(name)
            : await store.GetAsync(name, version, ct) is { } saved ? new ActivePrompt(saved.Version, saved.Text) : null;
        if (prompt is null)
            return null;

        var model = name == ActivePrompts.Relevance ? relevanceModel : extractionModel;
        var listValues = name == ActivePrompts.Extraction ? await lists.GetLatestAsync(ct) : null;
        var results = new List<PromptTryResult>();
        string? modelName = null;
        var stopped = false;
        foreach (var id in (articleIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaxTry))
        {
            var record = await ledger.GetAsync(id, ct);
            var (currentAnswer, currentReason, currentVersion) = await CurrentAsync(name, record, ct);
            PromptTryResult Result(string? answer, string? reason, string? error) =>
                new(id, record?.Title, answer, reason, error, currentAnswer, currentReason, currentVersion);

            if (stopped)
            {
                results.Add(Result(null, null, RateLimited));
                continue;
            }
            if (!model.IsConfigured)
            {
                results.Add(Result(null, null, NotConfigured));
                continue;
            }
            if (record is null || await parsed.GetLatestAsync(id, ct) is not { } article)
            {
                results.Add(Result(null, null, NoText));
                continue;
            }
            try
            {
                var text = name == ActivePrompts.Relevance
                    ? RelevancePrompt.Build(article, prompt.Template)
                    : ExtractionPrompt.Build(article, listValues, prompt.Template);
                var answer = await model.AnswerJsonAsync(text, ct);
                modelName = answer.Model;
                if (name == ActivePrompts.Relevance)
                {
                    var (verdict, reason) = RelevancePrompt.ReadAnswer(answer.Text);
                    results.Add(Result(verdict.ToString(), reason, null));
                }
                else
                {
                    var draft = ExtractionPrompt.ReadAnswer(answer.Text);
                    results.Add(Result(draft.Json, draft.Relevant ? draft.Summary : ExtractionAnswers.NoEvent, null));
                }
            }
            catch (ModelRateLimitedException)
            {
                stopped = true;
                results.Add(Result(null, null, RateLimited));
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                results.Add(Result(null, null, RateLimited));
            }
            catch (FormatException e)
            {
                results.Add(Result(null, null, $"{Unreadable}: {e.Message}"));
            }
        }
        return new PromptTryResponse(prompt.Version, modelName, results);
    }

    /// <summary>What the running prompt concluded: the ledger's verdict, or the latest draft.</summary>
    private async Task<(string? Answer, string? Reason, string? Version)> CurrentAsync(string name, LedgerRecord? record, CancellationToken ct)
    {
        if (record is null)
            return (null, null, null);
        if (name == ActivePrompts.Relevance)
        {
            return record.Stages.TryGetValue(Stage.Relevance, out var stage) && stage.Status == StageStatus.Succeeded
                ? (stage.Answer, stage.Reason, stage.Version)
                : (null, null, null);
        }
        return await drafts.GetLatestAsync(record.Id, ct) is { } draft
            ? (draft.Answer, draft.Relevant ? draft.Summary : ExtractionAnswers.NoEvent, draft.Version)
            : (null, null, null);
    }

    private static string? Trim(string? text, int length) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().Length <= length ? text.Trim() : text.Trim()[..length];
}
