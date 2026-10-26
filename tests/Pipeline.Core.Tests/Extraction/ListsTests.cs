using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;
using Pipeline.Core.Tests.Relevance;

namespace Pipeline.Core.Tests.Extraction;

internal sealed class InMemoryLists : IListStore
{
    public List<ListsSnapshot> Saved { get; } = [];

    public Task SaveAsync(ListsSnapshot lists, CancellationToken ct = default)
    {
        Saved.Add(lists);
        return Task.CompletedTask;
    }

    public Task<ListsSnapshot?> GetLatestAsync(CancellationToken ct = default) => Task.FromResult(Saved.LastOrDefault());
}

public class ListsTests
{
    private static Dictionary<string, IReadOnlyList<string>> AppLists(params string[] extraReasons) =>
        ExtractionPrompt.ListKeys.ToDictionary(k => k, k => (IReadOnlyList<string>)(k == "reasons" ? ["TİS", "Düşük ücret", .. extraReasons] : [$"{k} değeri"]));

    [Fact]
    public async Task ANewVersionIsKeptOnlyWhenTheListsChanged()
    {
        var store = new InMemoryLists();
        var update = new ListsUpdate(store, TimeProvider.System);

        var first = await update.PutAsync(new ListsRequest(AppLists()));
        var same = await update.PutAsync(new ListsRequest(AppLists(" TİS ", "")));   // only spacing, a duplicate and a blank differ
        var changed = await update.PutAsync(new ListsRequest(AppLists("Yeni neden")));

        Assert.Equal((true, false, true), (first.Changed, same.Changed, changed.Changed));
        Assert.Equal(first.UpdatedAt, same.UpdatedAt);
        Assert.Equal(2, store.Saved.Count);
        Assert.Equal(["Düşük ücret", "TİS", "Yeni neden"], store.Saved[^1].Lists["reasons"]);
        Assert.Equal(3, changed.Counts["reasons"]);
    }

    [Fact]
    public async Task AMissingOrEmptyListIsRefused()
    {
        var lists = AppLists();
        lists.Remove("cities");
        var update = new ListsUpdate(new InMemoryLists(), TimeProvider.System);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => update.PutAsync(new ListsRequest(lists)));
        Assert.Contains("cities", error.Message);
        var empty = AppLists();
        empty["reasons"] = ["  "];
        await Assert.ThrowsAsync<ArgumentException>(() => update.PutAsync(new ListsRequest(empty)));
    }

    [Fact]
    public void ThePromptOffersTheAppsListsOrTheSnapshot()
    {
        var article = new ParsedArticle("p1", "a1", "evrensel", "evrensel-2", DateTimeOffset.UtcNow, null, "Başlık", DateTimeOffset.UtcNow, "Metin.");
        var lists = new ListsSnapshot(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), ListsUpdate.Tidy(AppLists("Yeni neden")));

        Assert.Contains("- Yeni neden\n", ExtractionPrompt.Build(article, lists));
        Assert.DoesNotContain("Yeni neden", ExtractionPrompt.Build(article));
        Assert.Contains("- TİS\n", ExtractionPrompt.Build(article)); // the snapshot
        Assert.Equal(("lists 2026-10-26", "lists 2026-10-21"), (ExtractionPrompt.ListsLabel(lists), ExtractionPrompt.ListsLabel(null)));
    }

    [Fact]
    public async Task ADraftsVersionSaysWhichListsItWasMadeWith()
    {
        var ledger = new InMemoryLedger();
        var parsed = new InMemoryParsed();
        var drafts = new InMemoryDrafts();
        var store = new InMemoryLists();
        store.Saved.Add(new ListsSnapshot(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), ListsUpdate.Tidy(AppLists())));
        var at = DateTimeOffset.UtcNow;
        ledger.Records["a"] = new LedgerRecord("a", "evrensel", ArticleOrigin.Collector, "https://www.evrensel.net/haber/1",
            new Dictionary<Stage, StageResult> { [Stage.Parsed] = new(StageStatus.Succeeded, at) }, null, "Başlık", at, TurkeyTime.Normalize(at));
        parsed.Saved.Add(new ParsedArticle("p-a", "a", "evrensel", "evrensel-2", at, null, "Başlık", at, "Metin."));
        var model = new ScriptedModel(_ => new ModelAnswer("""{"relevant": true}""", "gemini-3.5-flash-lite"));

        await new DraftExtraction(ledger, parsed, drafts, model, new RecordingQueue(), TimeProvider.System, store)
            .HandleAsync(new ArticleRelevant("a", "evrensel", RelevanceVerdict.Relevant, at));

        Assert.Equal($"{ExtractionPrompt.Version} · lists 2026-10-26 · gemini-3.5-flash-lite", Assert.Single(drafts.Saved).Version);
    }
}
