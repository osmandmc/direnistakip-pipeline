using Pipeline.Core.Parsing;

namespace Pipeline.Core.Tests.Parsing;

public class EvrenselParserTests
{
    private readonly EvrenselParser _parser = new();

    private static string Fixture(string articleNumber) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "evrensel", $"{articleNumber}.html"));

    private static readonly TimeSpan Turkey = TimeSpan.FromHours(3);

    [Fact]
    public void ParsesALabourStory()
    {
        var result = _parser.Parse(Fixture("6005542"));

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal("Korteks'te iş kazası: İşçinin üç parmağı silindire sıkıştı", result.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 22, 12, 0, Turkey), result.PublishedAt);

        var paragraphs = result.Body!.Split("\n\n");
        Assert.StartsWith("Zorlu Holding’in sahibi olduğu Korteks’te", paragraphs[0]); // the lead comes first
        Assert.StartsWith("Bursa'da Zorlu Holding'in sahibi olduğu polyester iplik", paragraphs[1]);
        Assert.Contains("1300 işçinin çalıştığı", result.Body);
    }

    [Fact]
    public void KeepsSubheadingsAndListsAsParagraphs()
    {
        const string html = """
            <html><body><article><h1 class="articleTitle">Başlık</h1>
              <div class="news-content">
                <p>Giriş.</p>
                <h3>Toplumun dayattığı normlar sorgulanıyor</h3>
                <p>Devamı.</p>
                <ul><li>Birinci talep</li><li>İkinci talep</li></ul>
                <div class="evr-sub-cta"><p>Abone ol</p></div>
              </div>
            </article></body></html>
            """;

        var result = _parser.Parse(html);

        Assert.Equal(
            "Giriş.\n\nToplumun dayattığı normlar sorgulanıyor\n\nDevamı.\n\n- Birinci talep\n\n- İkinci talep",
            result.Body);
    }

    [Theory]
    [InlineData("6005542")]
    [InlineData("6005543")]
    [InlineData("6005544")]
    [InlineData("6005545")]
    [InlineData("6005546")]
    public void EverySampleParsesCleanly(string articleNumber)
    {
        var result = _parser.Parse(Fixture(articleNumber));

        Assert.True(result.Succeeded, result.Failure);
        Assert.DoesNotContain("Evrensel", result.Title!.Split(' ').TakeLast(1)); // no " - Evrensel" suffix
        Assert.Equal(Turkey, result.PublishedAt!.Value.Offset);
        Assert.True(result.Body!.Split("\n\n").Length >= 2, "lead plus at least one paragraph");

        // Nothing from the subscription box, ads or other stories on the page.
        Assert.DoesNotContain("abone", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Sesli Yazılar", result.Body);
        Assert.DoesNotContain("KYK", result.Body);
    }

    [Fact]
    public void FallsBackToOgTitleAndJsonLdDate()
    {
        const string html = """
            <html><head>
              <meta property="og:title" content="Fabrikada grev sürüyor - Evrensel">
              <script type="application/ld+json">{"@type":"NewsArticle","datePublished":"2026-10-01T09:30:00+03:00"}</script>
            </head><body><article>
              <div class="news-content"><p>İşçiler üçüncü gününde.</p></div>
            </article></body></html>
            """;

        var result = _parser.Parse(html);

        Assert.Equal("Fabrikada grev sürüyor", result.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, Turkey), result.PublishedAt);
        Assert.Equal("İşçiler üçüncü gününde.", result.Body);
    }

    [Fact]
    public void DateWithoutTimeZoneIsTurkeyTime()
    {
        const string html = """
            <html><head><meta name="article:published_time" content="2026-10-01T09:30:00"></head>
            <body><article><h1 class="articleTitle">Başlık</h1><div class="news-content"><p>Metin.</p></div></article></body></html>
            """;

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, Turkey), _parser.Parse(html).PublishedAt);
    }

    [Fact]
    public void MissingDateIsUnknownNotAFailure()
    {
        const string html = """
            <html><body><article><h1 class="articleTitle">Başlık</h1><div class="news-content"><p>Metin.</p></div></article></body></html>
            """;

        var result = _parser.Parse(html);

        Assert.True(result.Succeeded);
        Assert.Null(result.PublishedAt);
    }

    [Theory]
    [InlineData("<html><body><p>Bir sayfa</p></body></html>", "No <article> element")]
    [InlineData("<html><body><article><div class=\"news-content\"><p>Metin.</p></div></article></body></html>", "No title found")]
    [InlineData("<html><body><article><h1 class=\"articleTitle\">Başlık</h1><div class=\"news-content\"><div>reklam</div></div></article></body></html>", "No article body found")]
    public void UnusablePagesFailWithAReason(string html, string reason)
    {
        var result = _parser.Parse(html);

        Assert.False(result.Succeeded);
        Assert.Equal(reason, result.Failure);
    }
}
