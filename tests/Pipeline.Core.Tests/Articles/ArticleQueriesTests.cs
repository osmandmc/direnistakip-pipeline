using Pipeline.Contracts;
using Pipeline.Core.Articles;
using Pipeline.Core.Identity;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests.Articles;

public class ArticleQueriesTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly ArticleQueries _queries;

    public ArticleQueriesTests() => _queries = new(_ledger, _parsed);

    private void Add(string id, DateTimeOffset publishedAt, bool parsed = true, string? title = null)
    {
        var stages = new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, publishedAt) };
        if (parsed)
        {
            stages[Stage.Parsed] = new(StageStatus.Succeeded, publishedAt, "evrensel-1");
            _parsed.Saved.Add(new ParsedArticle($"p-{id}", id, "evrensel", "evrensel-1", publishedAt, null, $"Başlık {id}", publishedAt, $"Metin {id}",
                $"https://www.evrensel.net/images/{id}.jpg"));
        }
        _ledger.Records[id] = new LedgerRecord(
            id, "evrensel", ArticleOrigin.Csv, $"https://www.evrensel.net/haber/{id}", stages,
            Title: parsed ? title ?? $"Başlık {id}" : null, PublishedAt: parsed ? publishedAt : null, ListDate: publishedAt);
    }

    [Fact]
    public async Task DetailHasTheLatestText()
    {
        Add("a1", new DateTimeOffset(2026, 10, 18, 22, 12, 0, TurkeyTime.Offset));

        var detail = await _queries.GetAsync("a1");

        Assert.NotNull(detail);
        Assert.Equal("Başlık a1", detail.Title);
        Assert.Equal("Metin a1", detail.Body);
        Assert.Equal("evrensel-1", detail.ParserVersion);
        Assert.Equal("https://www.evrensel.net/images/a1.jpg", detail.ImageUrl);
        Assert.Contains(Stage.Parsed, detail.Stages.Keys);
    }

    [Fact]
    public async Task DetailBeforeParsingHasNoText()
    {
        Add("a2", DateTimeOffset.UtcNow, parsed: false);

        var detail = await _queries.GetAsync("a2");

        Assert.NotNull(detail);
        Assert.Null(detail.Body);
        Assert.Null(detail.ParserVersion);
    }

    [Fact]
    public async Task UnknownArticleIsNull() => Assert.Null(await _queries.GetAsync("nope"));

    [Fact]
    public async Task ListHasNoTextAndLoadsMore()
    {
        for (var day = 1; day <= 5; day++)
        {
            Add($"a{day}", new DateTimeOffset(2026, 10, day, 12, 0, 0, TurkeyTime.Offset));
        }

        var first = await _queries.ListAsync(new LedgerQuery(), pageSize: 3, next: null);
        var second = await _queries.ListAsync(new LedgerQuery(), pageSize: 3, next: first.Next);

        Assert.Equal(["a5", "a4", "a3"], first.Items.Select(i => i.ArticleId));
        Assert.Equal(["a2", "a1"], second.Items.Select(i => i.ArticleId));
        Assert.Null(second.Next);
        Assert.Equal("Başlık a5", first.Items[0].Title);
    }

    [Fact]
    public async Task PageSizeIsLimited()
    {
        for (var i = 0; i < 120; i++)
        {
            Add($"b{i}", DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        var page = await _queries.ListAsync(new LedgerQuery(), pageSize: 1000, next: null);

        Assert.Equal(ArticleQueries.MaxPageSize, page.Items.Count);
    }

    [Fact]
    public async Task TitleSearchIgnoresCaseAndTurkishLetters()
    {
        Add("c1", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TurkeyTime.Offset), title: "Korteks’te İŞÇİLER greve çıktı");
        Add("c2", new DateTimeOffset(2026, 10, 2, 12, 0, 0, TurkeyTime.Offset), title: "Belediye işçileri eylemde");
        Add("c3", new DateTimeOffset(2026, 10, 3, 12, 0, 0, TurkeyTime.Offset), title: "Hava durumu");

        var page = await _queries.ListAsync(new LedgerQuery(TitleContains: "isciler"), pageSize: 10, next: null);

        Assert.Equal(["c2", "c1"], page.Items.Select(i => i.ArticleId));
    }

    [Fact]
    public async Task LinkFindsItsArticleWhateverItsState()
    {
        var url = "https://www.evrensel.net/haber/560123/korteks";
        var id = ArticleIdentity.FromUrl(url);
        _ledger.Records[id] = new LedgerRecord(
            id, "evrensel", ArticleOrigin.Manual, url,
            new Dictionary<Stage, StageResult> { [Stage.Fetched] = new(StageStatus.Failed, DateTimeOffset.UtcNow, Reason: "HTTP 404") },
            Outcome: new ArticleOutcome(OutcomeKind.Dismissed, DateTimeOffset.UtcNow));

        // Same article: other host form and a tracking parameter.
        var found = await _queries.FindByUrlAsync("http://evrensel.net/haber/560123/korteks/?utm_source=x");
        var missing = await _queries.FindByUrlAsync("https://www.evrensel.net/haber/1/yok");

        Assert.Equal([id], found!.Items.Select(i => i.ArticleId));
        Assert.Empty(missing!.Items);
        Assert.Null(await _queries.FindByUrlAsync("korteks grev"));
    }
}
