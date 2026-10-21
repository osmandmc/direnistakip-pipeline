using Pipeline.Contracts;
using Pipeline.Core.Parsing;

namespace Pipeline.Core.Tests.Parsing;

public class ArticleReparsingTests
{
    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryRawHtml _rawHtml = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly RecordingQueue _queue = new();
    private readonly ArticleReparsing _reparsing;
    private static readonly DateTimeOffset Fetched = new(2026, 10, 20, 22, 56, 0, TimeSpan.Zero);

    public ArticleReparsingTests()
    {
        var parsing = new ArticleParsing([new EvrenselParser()], _ledger, _rawHtml, _parsed, _queue, TimeProvider.System);
        _reparsing = new ArticleReparsing(_ledger, parsing);
    }

    private static string Fixture(string number) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "evrensel", $"{number}.html"));

    /// <summary>A downloaded article whose parsing ended as given; its page is stored unless storePage is false.</summary>
    private async Task Add(string id, StageResult parse, string page, ArticleOutcome? outcome = null, bool fetched = true, bool storePage = true)
    {
        var stages = new Dictionary<Stage, StageResult>
        {
            [Stage.Submitted] = new(StageStatus.Succeeded, Fetched),
            [Stage.Fetched] = fetched ? new(StageStatus.Succeeded, Fetched) : new(StageStatus.Failed, Fetched, Reason: "HTTP 404"),
            [Stage.Parsed] = parse,
        };
        _ledger.Records[id] = new LedgerRecord(id, "evrensel", ArticleOrigin.Collector, $"https://www.evrensel.net/haber/{id}", stages, outcome,
            ListDate: TurkeyTime.Normalize(Fetched));
        if (storePage)
        {
            await _rawHtml.SaveAsync("evrensel", id, page, Fetched);
        }
    }

    private static StageResult Failed(string version = "evrensel-1") => new(StageStatus.Failed, Fetched, version, "No article body found");

    [Fact]
    public async Task AFailedArticleIsParsedAgainFromItsStoredPage()
    {
        await Add("a", Failed(), Fixture("6005790"));

        var result = await _reparsing.RunAsync(new ReparseQuery("evrensel"));

        Assert.Equal(new ReparseResponse(1, 1, 0, 0, false), result);
        Assert.Equal((StageStatus.Succeeded, new EvrenselParser().Version), (_ledger.Records["a"].Stages[Stage.Parsed].Status, _ledger.Records["a"].Stages[Stage.Parsed].Version));
        Assert.StartsWith("Orionid meteor yağmuru ne zaman", _ledger.Records["a"].Title);
        Assert.Single(_parsed.Saved);
        Assert.IsType<ArticleParsed>(Assert.Single(_queue.Sent).Message); // on to the inbox as usual
    }

    [Fact]
    public async Task HandledArticlesFailedDownloadsAndMissingPagesAreLeftAlone()
    {
        await Add("handled", Failed(), Fixture("6005790"), outcome: new ArticleOutcome(OutcomeKind.Dismissed, Fetched));
        await Add("download-failed", Failed(), "", fetched: false, storePage: false);
        await Add("no-page", Failed(), "", storePage: false);
        await Add("still-bad", Failed(), "<html><body><p>Sayfa yok</p></body></html>");

        var result = await _reparsing.RunAsync(new ReparseQuery("evrensel"));

        Assert.Equal(new ReparseResponse(Examined: 2, Parsed: 0, StillFailed: 1, NoStoredPage: 1, More: false), result);
        Assert.Equal(StageStatus.Failed, _ledger.Records["handled"].Stages[Stage.Parsed].Status); // untouched
        Assert.Equal("No <article> element", _ledger.Records["still-bad"].Stages[Stage.Parsed].Reason); // current parser's reason
    }

    [Fact]
    public async Task ArticlesReadByAnOlderParserVersionAreReadAgain()
    {
        await Add("old", new StageResult(StageStatus.Succeeded, Fetched, "evrensel-1"), Fixture("6005776"));
        await Add("current", new StageResult(StageStatus.Succeeded, Fetched, new EvrenselParser().Version), Fixture("6005776"));

        var result = await _reparsing.RunAsync(new ReparseQuery("evrensel", FailedOnly: false, ParserVersion: "evrensel-1"));

        Assert.Equal((1, 1), (result.Examined, result.Parsed));
        Assert.Equal(new EvrenselParser().Version, _ledger.Records["old"].Stages[Stage.Parsed].Version);
    }
}
