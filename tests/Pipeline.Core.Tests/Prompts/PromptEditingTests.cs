using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Prompts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;
using Pipeline.Core.Tests.Extraction;
using Pipeline.Core.Tests.Relevance;

namespace Pipeline.Core.Tests.Prompts;

internal sealed class InMemoryPrompts : IPromptStore
{
    public Dictionary<(string, string), PromptVersionRecord> Versions { get; } = [];
    public Dictionary<string, PromptVersionRecord> Active { get; } = [];

    public Task SaveAsync(PromptVersionRecord version, CancellationToken ct = default)
    {
        if (!Versions.TryAdd((version.Name, version.Version), version))
            throw new InvalidOperationException("exists");
        return Task.CompletedTask;
    }

    public Task<PromptVersionRecord?> GetAsync(string name, string version, CancellationToken ct = default) =>
        Task.FromResult(Versions.GetValueOrDefault((name, version)));

    public Task<IReadOnlyList<PromptVersionRecord>> ListAsync(string name, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PromptVersionRecord>>(Versions.Values.Where(v => v.Name == name).OrderByDescending(v => v.CreatedAt).ToList());

    public Task<PromptVersionRecord?> GetActiveAsync(string name, CancellationToken ct = default) => Task.FromResult(Active.GetValueOrDefault(name));

    public Task SetActiveAsync(string name, PromptVersionRecord? version, CancellationToken ct = default)
    {
        if (version is null)
            Active.Remove(name);
        else
            Versions[(name, version.Version)] = Active[name] = version;
        return Task.CompletedTask;
    }
}

/// <summary>B17: prompt versions edited in the app.</summary>
public class PromptEditingTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPrompts _store = new();
    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly InMemoryDrafts _drafts = new();
    private readonly ActivePrompts _active;

    public PromptEditingTests() => _active = new ActivePrompts(_store, TimeProvider.System);

    private static ScriptedModel Answers(string json) => new(_ => new ModelAnswer(json, "gemini-flash-lite"));

    private PromptEditing Editing(ILanguageModel? relevance = null, ILanguageModel? extraction = null) =>
        new(_store, _active, _ledger, _parsed, _drafts, new InMemoryLists(), relevance ?? Answers("{}"), extraction ?? Answers("{}"), TimeProvider.System);

    private const string RelevanceText = "Yeni talimat. {{SOURCE}} {{DATE}} {{TITLE}}\n{{TEXT}}";

    private void AddArticle(string id, StageResult? relevance = null)
    {
        var stages = new Dictionary<Stage, StageResult> { [Stage.Parsed] = new(StageStatus.Succeeded, At, "evrensel-2") };
        if (relevance is not null)
            stages[Stage.Relevance] = relevance;
        _ledger.Records[id] = new LedgerRecord(id, "evrensel", ArticleOrigin.Collector, $"https://x/{id}", stages, null, "Başlık " + id, At, TurkeyTime.Normalize(At));
        _parsed.Saved.Add(new ParsedArticle("p-" + id, id, "evrensel", "evrensel-2", At, null, "İşçiler iş bıraktı", At, "Fabrikada işçiler bugün iş bıraktı."));
    }

    [Fact]
    public async Task VersionsAreNamedAfterTheBuiltInPromptAndKeepThePlaceholders()
    {
        var first = await Editing().CreateAsync("relevance", new PromptVersionRequest(RelevanceText, "Kısa", "ayse"));
        var second = await Editing().CreateAsync("relevance", new PromptVersionRequest(RelevanceText + " ek", null, "ayse"));

        Assert.Equal(($"{RelevancePrompt.Version}-e1", $"{RelevancePrompt.Version}-e2"), (first.Version, second.Version));
        Assert.Equal(("Kısa", "ayse"), (first.Note, first.CreatedBy));
        var missing = await Assert.ThrowsAsync<ArgumentException>(() => Editing().CreateAsync("extraction", new PromptVersionRequest("{{TITLE}} {{TEXT}}", null, null)));
        Assert.Contains("{{LISTS}}", missing.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => Editing().CreateAsync("other", new PromptVersionRequest(RelevanceText, null, null)));
    }

    [Fact]
    public async Task TheActiveVersionJudgesNewArticlesAndMakesEarlierVerdictsOlder()
    {
        var version = await Editing().CreateAsync("relevance", new PromptVersionRequest(RelevanceText, null, null));
        await Editing().ActivateAsync("relevance", version.Version, "ayse");
        AddArticle("a");
        var model = Answers("""{"verdict":"relevant","reason":"Grev."}""");

        await new RelevanceAssessment(_ledger, _parsed, new InMemoryRelevance(), model, new RecordingQueue(), TimeProvider.System, _active)
            .AssessAsync(_ledger.Records["a"]);

        Assert.StartsWith("Yeni talimat. Evrensel", Assert.Single(model.Prompts));
        Assert.Equal($"{version.Version} · gemini-flash-lite", _ledger.Records["a"].Stages[Stage.Relevance].Version);
        var judgedBefore = _ledger.Records["a"] with
        {
            Stages = new Dictionary<Stage, StageResult> { [Stage.Relevance] = new(StageStatus.Succeeded, At, $"{RelevancePrompt.Version} · gemini") },
        };
        Assert.True(RelevancePrompt.IsOlder(judgedBefore, (await _active.GetAsync("relevance")).Version));
        Assert.Equal("ayse", _store.Active["relevance"].ActivatedBy);

        await Editing().ActivateAsync("relevance", PromptEditing.BuiltinVersion, null);
        Assert.Equal(RelevancePrompt.Version, (await _active.GetAsync("relevance")).Version);
        Assert.Null(await Editing().ActivateAsync("relevance", "relevance-v9-e9", null));
    }

    [Fact]
    public async Task ATryRunsTheVersionWithoutSavingAndShowsTheCurrentVerdict()
    {
        var version = await Editing().CreateAsync("relevance", new PromptVersionRequest(RelevanceText, null, null));
        AddArticle("a", new StageResult(StageStatus.Succeeded, At, $"{RelevancePrompt.Version} · gemini", "Analiz.", "NotRelevant"));
        AddArticle("b");
        var model = Answers("""{"verdict":"relevant","reason":"Grev."}""");

        var tried = await Editing(relevance: model).TryAsync("relevance", version.Version, ["a", "b", "missing", "a"]);

        Assert.Equal(version.Version, tried!.Version);
        Assert.Equal(
            [
                ("a", "Relevant", "Grev.", null, "NotRelevant"),
                ("b", "Relevant", "Grev.", null, null),
                ("missing", null, null, PromptEditing.NoText, null),
            ],
            tried.Results.Select(r => (r.ArticleId, r.Answer, r.Reason, r.Error, r.CurrentAnswer)));
        Assert.Equal("Başlık a", tried.Results[0].Title);
        Assert.Equal("NotRelevant", _ledger.Records["a"].Stages[Stage.Relevance].Answer); // nothing saved
        Assert.Null(await Editing().TryAsync("relevance", "relevance-v9-e9", ["a"]));
    }

    [Fact]
    public async Task ATryStopsAskingWhenTheQuotaRunsOut()
    {
        AddArticle("a");
        AddArticle("b");
        var model = new ScriptedModel(_ => throw new ModelRateLimitedException(TimeSpan.FromMinutes(1), "429"));

        var tried = await Editing(relevance: model).TryAsync("relevance", PromptEditing.BuiltinVersion, ["a", "b"]);

        Assert.Equal([PromptEditing.RateLimited, PromptEditing.RateLimited], tried!.Results.Select(r => r.Error));
        Assert.Single(model.Prompts);
    }

    [Fact]
    public async Task AnExtractionTryReturnsTheDraftAndTheLatestOne()
    {
        AddArticle("a");
        _drafts.Saved.Add(new DraftRecord("d", "a", "p-a", "prompt-v2.1 · lists 2026-10-21 · gemini", """{"relevant":false}""", false, null, At));
        var model = Answers("""{"relevant":true,"case":{"company":"Örnek Metal"},"protest":{"types":["Fiili grev"]}}""");

        var tried = await Editing(extraction: model).TryAsync("extraction", PromptEditing.BuiltinVersion, ["a"]);

        var result = Assert.Single(tried!.Results);
        Assert.Equal(("Örnek Metal · Fiili grev", ExtractionAnswers.NoEvent), (result.Reason, result.CurrentReason));
        Assert.Contains("Örnek Metal", result.Answer);
        Assert.Contains("Fabrikada işçiler", Assert.Single(model.Prompts));
    }
}
