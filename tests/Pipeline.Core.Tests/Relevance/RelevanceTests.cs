using System.Net;
using System.Text;
using Pipeline.Contracts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests.Relevance;

/// <summary>A model that answers with the given function, or throws what it returns as an exception.</summary>
internal sealed class ScriptedModel(Func<string, ModelAnswer> answer, bool configured = true) : ILanguageModel
{
    public List<string> Prompts { get; } = [];
    public bool IsConfigured => configured;

    public Task<ModelAnswer> AnswerJsonAsync(string prompt, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        return Task.FromResult(answer(prompt));
    }
}

internal sealed class InMemoryRelevance : IRelevanceStore
{
    public List<RelevanceRecord> Saved { get; } = [];

    public Task SaveAsync(RelevanceRecord record, CancellationToken ct = default)
    {
        Saved.Add(record);
        return Task.CompletedTask;
    }
}

public class RelevancePromptTests
{
    [Fact]
    public void ThePromptHoldsTheArticleAndOnlyTheStartOfLongTexts()
    {
        var body = string.Join(' ', Enumerable.Repeat("işçiler", 400));

        var prompt = RelevancePrompt.Build("Evrensel", new DateTimeOffset(2026, 10, 23, 23, 30, 0, TimeSpan.Zero), "Başlık", body);

        Assert.Contains("Kaynak: Evrensel · Tarih: 2026-10-24 · Başlık: Başlık", prompt); // Turkey time
        Assert.Contains("işçiler […]", prompt);
        Assert.DoesNotContain("{{", prompt);
        Assert.True(prompt.Length < 2000 + RelevancePrompt.TextLength);
    }

    [Theory]
    [InlineData("""{"verdict": "relevant", "reason": "Grev var."}""", RelevanceVerdict.Relevant, "Grev var.")]
    [InlineData("```json\n{ \"verdict\": \"not_relevant\", \"reason\": \" Analiz yazısı. \" }\n```", RelevanceVerdict.NotRelevant, "Analiz yazısı.")]
    [InlineData("""{"verdict": "Uncertain"}""", RelevanceVerdict.Uncertain, null)]
    public void TheAnswerIsRead(string answer, RelevanceVerdict verdict, string? reason) =>
        Assert.Equal((verdict, reason), RelevancePrompt.ReadAnswer(answer));

    [Theory]
    [InlineData("Bu haber ilgili.")]
    [InlineData("""{"verdict": "maybe"}""")]
    [InlineData("""{"verdict": "relevant", """)]
    public void AnUnreadableAnswerIsAFormatError(string answer) =>
        Assert.Throws<FormatException>(() => RelevancePrompt.ReadAnswer(answer));
}

public class GeminiModelTests
{
    private static GeminiModel Model(Func<HttpRequestMessage, HttpResponseMessage> respond, string? key = "k") =>
        new(new HttpClient(new StubHttpHandler(respond)), new GeminiOptions { ApiKey = key, Model = "gemini-flash-lite-latest" });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task TheAnswerTextAndTheExactModelAreReturned()
    {
        HttpRequestMessage? sent = null;
        var model = Model(request =>
        {
            sent = request;
            return Json(HttpStatusCode.OK, """{"candidates":[{"content":{"parts":[{"text":"{\"verdict\":\"relevant\"}"}]}}],"modelVersion":"gemini-3-flash"}""");
        });

        var answer = await model.AnswerJsonAsync("soru");

        Assert.Equal(new ModelAnswer("""{"verdict":"relevant"}""", "gemini-3-flash"), answer);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-lite-latest:generateContent", sent!.RequestUri!.ToString());
        Assert.Equal("k", sent.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task TooManyRequestsSaysHowLongToWait()
    {
        var model = Model(_ => Json(HttpStatusCode.TooManyRequests,
            """{"error":{"code":429,"message":"Quota exceeded","details":[{"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"37.2s"}]}}"""));

        var error = await Assert.ThrowsAsync<ModelRateLimitedException>(() => model.AnswerJsonAsync("soru"));

        Assert.Equal(TimeSpan.FromSeconds(38), error.RetryAfter);
    }

    [Fact]
    public async Task ABusyModelIsAWaitLikeARateLimit()
    {
        var model = Model(_ => Json(HttpStatusCode.ServiceUnavailable, """{"error":{"message":"high demand"}}"""));

        var error = await Assert.ThrowsAsync<ModelRateLimitedException>(() => model.AnswerJsonAsync("soru"));

        Assert.Equal(GeminiModel.DefaultRetryAfter, error.RetryAfter);
        Assert.Contains("high demand", error.Message);
    }

    [Fact]
    public async Task OtherErrorsThrowForTheQueueToRetry()
    {
        var model = Model(_ => Json(HttpStatusCode.InternalServerError, """{"error":{"message":"internal"}}"""));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => model.AnswerJsonAsync("soru"));

        Assert.Contains("internal", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@Microsoft.KeyVault(VaultName=kv;SecretName=gemini-api-key)")]
    public void WithoutAKeyTheModelIsOff(string? key) =>
        Assert.False(Model(_ => throw new InvalidOperationException(), key).IsConfigured);
}

public class RelevanceAssessmentTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly InMemoryRelevance _relevance = new();
    private readonly RecordingQueue _queue = new();
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    private RelevanceAssessment Assessment(ILanguageModel model) => new(_ledger, _parsed, _relevance, model, _queue, TimeProvider.System);

    private static ScriptedModel Answers(string json) => new(_ => new ModelAnswer(json, "gemini-3-flash"));

    private ArticleParsed Add(string id, ArticleOrigin origin = ArticleOrigin.Collector, ArticleOutcome? outcome = null, Stage? done = null)
    {
        var stages = new Dictionary<Stage, StageResult> { [Stage.Parsed] = new(StageStatus.Succeeded, At, "evrensel-2") };
        if (done is { } stage)
        {
            stages[stage] = new(StageStatus.Succeeded, At, "relevance-v1", Answer: "Relevant");
        }
        _ledger.Records[id] = new LedgerRecord(id, "evrensel", origin, $"https://www.evrensel.net/haber/{id}", stages, outcome, "Başlık", At, TurkeyTime.Normalize(At));
        _parsed.Saved.Add(new ParsedArticle("p-" + id, id, "evrensel", "evrensel-2", At, null, "İşçiler iş bıraktı", At, "Fabrikada işçiler bugün iş bıraktı."));
        return new ArticleParsed(id, "evrensel", "p-" + id, "evrensel-2", At);
    }

    [Fact]
    public async Task TheVerdictIsSavedAndGoesIntoTheLedger()
    {
        var message = Add("a");
        var model = Answers("""{"verdict":"not_relevant","reason":"Analiz yazısı."}""");

        var outcome = await Assessment(model).HandleAsync(message);

        Assert.Equal(AssessOutcome.Assessed, outcome);
        Assert.Contains("İşçiler iş bıraktı", Assert.Single(model.Prompts));
        var saved = Assert.Single(_relevance.Saved);
        Assert.Equal(("a", "p-a", RelevanceVerdict.NotRelevant, "Analiz yazısı.", "relevance-v1 · gemini-3-flash"),
            (saved.ArticleId, saved.ParsedId, saved.Verdict, saved.Reason, saved.Version));
        var stage = _ledger.Records["a"].Stages[Stage.Relevance];
        Assert.Equal((StageStatus.Succeeded, "NotRelevant", "Analiz yazısı.", "relevance-v1 · gemini-3-flash"), (stage.Status, stage.Answer, stage.Reason, stage.Version));
        Assert.Empty(_queue.Sent);
    }

    [Fact]
    public async Task APastedLinkIsSkippedWithoutAskingTheModel()
    {
        var message = Add("a", ArticleOrigin.Manual);
        var model = Answers("{}");

        Assert.Equal(AssessOutcome.Skipped, await Assessment(model).HandleAsync(message));

        Assert.Empty(model.Prompts);
        Assert.Equal(StageStatus.Skipped, _ledger.Records["a"].Stages[Stage.Relevance].Status);
    }

    [Fact]
    public async Task NothingHappensWithoutAKeyOrForArticlesAlreadyHandledOrAssessed()
    {
        var model = Answers("""{"verdict":"relevant"}""");
        var handled = Add("handled", outcome: new ArticleOutcome(OutcomeKind.Dismissed, At));
        var assessed = Add("assessed", done: Stage.Relevance);
        var fresh = Add("fresh");

        Assert.Equal(AssessOutcome.NotRun, await Assessment(model).HandleAsync(handled));
        Assert.Equal(AssessOutcome.NotRun, await Assessment(model).HandleAsync(assessed));
        Assert.Equal(AssessOutcome.NotRun, await Assessment(new ScriptedModel(_ => throw new InvalidOperationException(), configured: false)).HandleAsync(fresh));

        Assert.Empty(model.Prompts);
        Assert.False(_ledger.Records["fresh"].Stages.ContainsKey(Stage.Relevance));
    }

    [Fact]
    public async Task ARateLimitSendsTheArticleBackForLater()
    {
        var message = Add("a");
        var model = new ScriptedModel(_ => throw new ModelRateLimitedException(TimeSpan.FromSeconds(40), "429"));

        Assert.Equal(AssessOutcome.Deferred, await Assessment(model).HandleAsync(message));

        var later = Assert.Single(_queue.SentLater);
        Assert.Equal((QueueNames.ArticleParsed, (object?)message), (later.Queue, later.Message));
        Assert.InRange(later.Delay, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(40) + RelevanceAssessment.MaxJitter);
        Assert.False(_ledger.Records["a"].Stages.ContainsKey(Stage.Relevance));
    }

    [Fact]
    public async Task AModelThatDoesNotAnswerInTimeIsTriedLater()
    {
        var message = Add("a");
        var model = new ScriptedModel(_ => throw new TaskCanceledException("timeout"));

        Assert.Equal(AssessOutcome.Deferred, await Assessment(model).HandleAsync(message));

        Assert.InRange(Assert.Single(_queue.SentLater).Delay, RelevanceAssessment.SlowAnswerRetryAfter, RelevanceAssessment.SlowAnswerRetryAfter + RelevanceAssessment.MaxJitter);
    }

    [Fact]
    public async Task AnUnreadableAnswerIsAFailedStageThatCanBeTriedAgain()
    {
        var message = Add("a");

        Assert.Equal(AssessOutcome.Failed, await Assessment(Answers("Bilmiyorum.")).HandleAsync(message));
        Assert.Equal(StageStatus.Failed, _ledger.Records["a"].Stages[Stage.Relevance].Status);

        Assert.Equal(AssessOutcome.Assessed, await Assessment(Answers("""{"verdict":"uncertain"}""")).HandleAsync(message));
        Assert.Equal("Uncertain", _ledger.Records["a"].Stages[Stage.Relevance].Answer);
    }

    [Fact]
    public async Task TheBackfillQueuesOpenArticlesWithoutAVerdictSpacedOut()
    {
        Add("a");
        Add("b");
        Add("assessed", done: Stage.Relevance);
        Add("pasted", ArticleOrigin.Manual);
        Add("handled", outcome: new ArticleOutcome(OutcomeKind.Digested, At));

        var result = await new ArticleAssessing(_ledger, _parsed, Answers("{}"), _queue).RunAsync("evrensel");
        var off = await new ArticleAssessing(_ledger, _parsed, new ScriptedModel(_ => throw new InvalidOperationException(), configured: false), _queue).RunAsync("evrensel");

        Assert.Equal(new AssessResponse(2, 9, false, true), result);
        Assert.Equal(["a", "b"], _queue.SentLater.Select(s => ((ArticleParsed)s.Message!).ArticleId).Order());
        Assert.Equal([TimeSpan.Zero, ArticleAssessing.Spacing], _queue.SentLater.Select(s => s.Delay).Order());
        Assert.Equal(new AssessResponse(0, 9, false, false), off);
    }
}
