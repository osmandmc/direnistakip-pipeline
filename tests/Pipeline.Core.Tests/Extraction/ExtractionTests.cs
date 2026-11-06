using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;
using Pipeline.Core.Tests.Relevance;

namespace Pipeline.Core.Tests.Extraction;

internal sealed class InMemoryDrafts : IDraftStore
{
    public List<DraftRecord> Saved { get; } = [];

    public Task SaveAsync(DraftRecord draft, CancellationToken ct = default)
    {
        Saved.Add(draft);
        return Task.CompletedTask;
    }

    public Task<DraftRecord?> GetLatestAsync(string articleId, CancellationToken ct = default) =>
        Task.FromResult(Saved.LastOrDefault(d => d.ArticleId == articleId));
}

public class ExtractionPromptTests
{
    private static readonly ParsedArticle Article = new("p1", "a1", "evrensel", "evrensel-2", DateTimeOffset.UtcNow, null,
        "Örnek Metal işçileri iş bıraktı", new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(3)), "Gebze'de işçiler bugün iki saat iş bıraktı.");

    [Fact]
    public void ThePromptHoldsTheArticleAndTheLists()
    {
        var prompt = ExtractionPrompt.Build(Article);

        Assert.Contains("Kaynak: Evrensel · Tarih: 2026-10-14 · Başlık: Örnek Metal işçileri iş bıraktı", prompt);
        Assert.Contains("Gebze'de işçiler bugün iki saat iş bıraktı.", prompt);
        Assert.Contains("### Direniş nedenleri\n", prompt);
        Assert.Contains("- TİS\n", prompt);
        Assert.Contains("### İller\n", prompt);
        Assert.DoesNotContain("{{", prompt);
    }

    [Fact]
    public void TheAnswerGivesTheJsonAndAOneLineSummary()
    {
        var answer = """
            Bilgiler:
            ```json
            { "relevant": true, "case": { "company": "Örnek Metal" },
              "protest": { "start_date": "2026-10-14", "types": ["Fiili grev", "Basın açıklaması"], "locations": [{ "city": "Kocaeli", "district": "Gebze" }] } }
            ```
            """;

        var (json, relevant, summary) = ExtractionPrompt.ReadAnswer(answer);

        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
        Assert.True(relevant);
        Assert.Equal("Örnek Metal · Fiili grev · Kocaeli / Gebze · 2026-10-14", summary);
    }

    [Fact]
    public void NoActionMeansNoSummary() =>
        Assert.Equal((false, (string?)null), ExtractionPrompt.ReadAnswer("""{"relevant": false, "relevance_reason": "Analiz."}""") is var r ? (r.Relevant, r.Summary) : default);

    [Fact]
    public void AnAnswerWithoutJsonIsAFormatError() =>
        Assert.Throws<FormatException>(() => ExtractionPrompt.ReadAnswer("Bu haberde eylem yok."));
}

public class DraftExtractionTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly InMemoryDrafts _drafts = new();
    private readonly RecordingQueue _queue = new();
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private DraftExtraction Extraction(ILanguageModel model) => new(_ledger, _parsed, _drafts, model, _queue, TimeProvider.System);

    private static ScriptedModel Answers(string json) => new(_ => new ModelAnswer(json, "gemini-3-flash"));

    private ArticleRelevant Add(string id, string? relevance = "Relevant", ArticleOutcome? outcome = null, bool drafted = false,
        string draftVersion = "prompt-v2 · lists 2026-10-21 · gemini", string draftAnswer = ExtractionAnswers.Draft)
    {
        var stages = new Dictionary<Stage, StageResult> { [Stage.Parsed] = new(StageStatus.Succeeded, At, "evrensel-2") };
        if (relevance is not null)
        {
            stages[Stage.Relevance] = new(StageStatus.Succeeded, At, "relevance-v1", "…", relevance);
        }
        if (drafted)
        {
            stages[Stage.Extracted] = new(StageStatus.Succeeded, At, draftVersion, "…", draftAnswer);
        }
        _ledger.Records[id] = new LedgerRecord(id, "evrensel", ArticleOrigin.Collector, $"https://www.evrensel.net/haber/{id}", stages, outcome, "Başlık", At, TurkeyTime.Normalize(At));
        _parsed.Saved.Add(new ParsedArticle("p-" + id, id, "evrensel", "evrensel-2", At, null, "Başlık", At, "Metin."));
        return new ArticleRelevant(id, "evrensel", RelevanceVerdict.Relevant, At);
    }

    [Fact]
    public async Task ADraftIsSavedAndTheLedgerSaysDraftWithTheSummary()
    {
        var message = Add("a");
        var model = Answers("""{"relevant": true, "case": {"company": "Örnek Metal"}, "protest": {"types": ["Fiili grev"]}}""");

        Assert.Equal(AssessOutcome.Assessed, await Extraction(model).HandleAsync(message));

        var draft = Assert.Single(_drafts.Saved);
        Assert.Equal(("a", "p-a", true, "Örnek Metal · Fiili grev", $"{ExtractionPrompt.Version} · lists 2026-10-21 · gemini-3-flash"),
            (draft.ArticleId, draft.ParsedId, draft.Relevant, draft.Summary, draft.Version));
        var stage = _ledger.Records["a"].Stages[Stage.Extracted];
        Assert.Equal((StageStatus.Succeeded, ExtractionAnswers.Draft, "Örnek Metal · Fiili grev"), (stage.Status, stage.Answer, stage.Reason));
    }

    [Fact]
    public async Task NoActionFoundIsRecordedAsNoEvent()
    {
        var message = Add("a");

        await Extraction(Answers("""{"relevant": false, "relevance_reason": "Röportaj."}""")).HandleAsync(message);

        Assert.Equal(ExtractionAnswers.NoEvent, _ledger.Records["a"].Stages[Stage.Extracted].Answer);
        Assert.False(Assert.Single(_drafts.Saved).Relevant);
    }

    [Fact]
    public async Task HandledDraftedOrKeylessArticlesAreLeftAlone()
    {
        var model = Answers("""{"relevant": true}""");

        Assert.Equal(AssessOutcome.NotRun, await Extraction(model).HandleAsync(Add("handled", outcome: new ArticleOutcome(OutcomeKind.Dismissed, At))));
        Assert.Equal(AssessOutcome.NotRun, await Extraction(model).HandleAsync(Add("drafted", drafted: true)));
        Assert.Equal(AssessOutcome.NotRun, await Extraction(new ScriptedModel(_ => throw new InvalidOperationException(), configured: false)).HandleAsync(Add("fresh")));
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public async Task ABusyModelSendsTheArticleBackToItsOwnQueue()
    {
        var message = Add("a");

        var outcome = await Extraction(new ScriptedModel(_ => throw new ModelRateLimitedException(TimeSpan.FromMinutes(1), "busy"))).HandleAsync(message);

        Assert.Equal(AssessOutcome.Deferred, outcome);
        Assert.Equal(QueueNames.ArticleRelevant, Assert.Single(_queue.SentLater).Queue);
    }

    [Fact]
    public async Task TheRelevanceStageHandsRelevantAndUncertainArticlesOn()
    {
        var relevance = new InMemoryRelevance();
        foreach (var (id, verdict) in new[] { ("r", "relevant"), ("u", "uncertain"), ("n", "not_relevant") })
        {
            Add(id, relevance: null);
            var model = Answers($$"""{"verdict":"{{verdict}}"}""");
            await new RelevanceAssessment(_ledger, _parsed, relevance, model, _queue, TimeProvider.System)
                .HandleAsync(new ArticleParsed(id, "evrensel", "p-" + id, "evrensel-2", At));
        }

        Assert.Equal(["r", "u"], _queue.Sent.Where(s => s.Queue == QueueNames.ArticleRelevant).Select(s => ((ArticleRelevant)s.Message!).ArticleId));
    }

    [Fact]
    public async Task TheBackfillQueuesOpenRelevantArticlesWithoutADraft()
    {
        Add("relevant");
        Add("uncertain", "Uncertain");
        Add("rejected", "NotRelevant");
        Add("unassessed", relevance: null);
        Add("drafted", drafted: true);

        var result = await new ArticleDrafting(_ledger, Answers("{}"), _queue).RunAsync("evrensel");

        Assert.Equal(new DraftQueueResponse(2, 10, false, true), result);
        Assert.Equal(["relevant", "uncertain"], _queue.SentLater.Select(s => ((ArticleRelevant)s.Message!).ArticleId).Order());
    }

    [Fact]
    public async Task AskedAgainOnlyADraftByAnOlderPromptIsDraftedAgain()
    {
        var model = Answers("""{"relevant": true, "case": {"company": "Örnek Metal"}}""");
        var older = Add("older", drafted: true) with { Redraft = true };
        var current = Add("current", drafted: true, draftVersion: $"{ExtractionPrompt.Version} · lists 2026-11-06 · gemini") with { Redraft = true };

        Assert.Equal(AssessOutcome.Assessed, await Extraction(model).HandleAsync(older));
        Assert.Equal(AssessOutcome.NotRun, await Extraction(model).HandleAsync(current));
        Assert.Equal(AssessOutcome.NotRun, await Extraction(model).HandleAsync(older)); // delivered twice: now current

        Assert.Equal("older", Assert.Single(_drafts.Saved).ArticleId);
        Assert.StartsWith(ExtractionPrompt.Version + " ", _ledger.Records["older"].Stages[Stage.Extracted].Version);
    }

    [Fact]
    public async Task AgainQueuesOpenArticlesDraftedByAnOlderPrompt()
    {
        Add("older-draft", drafted: true);
        Add("older-no-event", drafted: true, draftVersion: "prompt-v2.1-e2 · lists 2026-11-01 · gemini", draftAnswer: ExtractionAnswers.NoEvent);
        Add("edited-on-current", drafted: true, draftVersion: $"{ExtractionPrompt.Version}-e1 · lists 2026-11-06 · gemini");
        Add("current", drafted: true, draftVersion: $"{ExtractionPrompt.Version} · lists 2026-11-06 · gemini");
        Add("handled", drafted: true, outcome: new ArticleOutcome(OutcomeKind.Dismissed, At));
        Add("rejected-since", "NotRelevant", drafted: true);
        Add("not-drafted");

        var result = await new ArticleDrafting(_ledger, Answers("{}"), _queue).RunAsync("evrensel", again: true);

        Assert.Equal(new DraftQueueResponse(3, 10, false, true), result);
        var sent = _queue.SentLater.Select(s => (ArticleRelevant)s.Message!).ToList();
        Assert.Equal(["edited-on-current", "older-draft", "older-no-event"], sent.Select(m => m.ArticleId).Order());
        Assert.All(sent, m => Assert.True(m.Redraft));
    }
}
