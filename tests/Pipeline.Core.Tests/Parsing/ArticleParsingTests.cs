using Pipeline.Contracts;
using Pipeline.Core.Parsing;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests.Parsing;

public class ArticleParsingTests
{
    private const string ArticleId = "a1";
    private const string GoodHtml = """
        <html><body><article><h1 class="articleTitle">Başlık</h1>
        <div class="news-content"><p>Birinci paragraf.</p></div></article></body></html>
        """;

    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryRawHtml _rawHtml = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly RecordingQueue _queue = new();
    private readonly ArticleParsing _parsing;

    public ArticleParsingTests()
    {
        _parsing = new ArticleParsing([new EvrenselParser()], _ledger, _rawHtml, _parsed, _queue, TimeProvider.System);
        _ledger.Records[ArticleId] = new LedgerRecord(
            ArticleId, "evrensel", ArticleOrigin.Manual, "https://www.evrensel.net/haber/1/x",
            new Dictionary<Stage, StageResult>
            {
                [Stage.Submitted] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow),
                [Stage.Fetched] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow),
            });
    }

    private async Task<ArticleFetched> Fetched(string html, string sourceId = "evrensel")
    {
        var path = await _rawHtml.SaveAsync(sourceId, ArticleId, html, DateTimeOffset.UtcNow);
        return new ArticleFetched(ArticleId, sourceId, path, "https://www.evrensel.net/haber/1/x", DateTimeOffset.UtcNow);
    }

    private StageResult Parsed => _ledger.Records[ArticleId].Stages[Stage.Parsed];

    [Fact]
    public async Task SavesTheParsedArticleAndHandsItOn()
    {
        var fetched = await Fetched(GoodHtml);

        await _parsing.ParseAsync(fetched);

        var saved = Assert.Single(_parsed.Saved);
        Assert.Equal(new EvrenselParser().Version, saved.ParserVersion);
        Assert.Equal(fetched.RawHtmlPath, saved.RawHtmlPath);
        Assert.Equal("Başlık", saved.Title);
        Assert.Equal("Birinci paragraf.", saved.Body);

        Assert.Equal(StageStatus.Succeeded, Parsed.Status);
        Assert.Equal(new EvrenselParser().Version, Parsed.Version);
        Assert.Equal("Başlık", _ledger.Records[ArticleId].Title); // copied for the inbox list

        var (queueName, message) = Assert.Single(_queue.Sent);
        Assert.Equal(QueueNames.ArticleParsed, queueName);
        Assert.Equal(saved.Id, Assert.IsType<ArticleParsed>(message).ParsedId);
    }

    [Fact]
    public async Task UnusablePageIsAFailureWithReasonAndNoRetry()
    {
        await _parsing.ParseAsync(await Fetched("<html><body>Sayfa bulunamadı</body></html>"));

        Assert.Equal(StageStatus.Failed, Parsed.Status);
        Assert.Equal("No <article> element", Parsed.Reason);
        Assert.Empty(_parsed.Saved);
        Assert.Empty(_queue.Sent);
    }

    [Fact]
    public async Task AlreadyParsedIsSkipped()
    {
        var fetched = await Fetched(GoodHtml);
        await _parsing.ParseAsync(fetched);

        await _parsing.ParseAsync(fetched); // delivered twice

        Assert.Single(_parsed.Saved);
        Assert.Single(_queue.Sent);
    }

    [Fact]
    public async Task SourceWithoutParserIsAFailure()
    {
        await _parsing.ParseAsync(await Fetched(GoodHtml, sourceId: "birgun"));

        Assert.Equal("No parser for source 'birgun'", Parsed.Reason);
    }

    [Fact]
    public async Task UnknownArticleIsIgnored()
    {
        var fetched = await Fetched(GoodHtml);

        await _parsing.ParseAsync(fetched with { ArticleId = "not-in-ledger" });

        Assert.Empty(_parsed.Saved);
        Assert.Empty(_queue.Sent);
    }
}
