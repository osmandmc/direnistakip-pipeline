using Pipeline.Core.Parsing;

namespace Pipeline.Core.Tests.Parsing;

public class SendikaParserTests
{
    private readonly SendikaParser _parser = new();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sendika", name));

    [Fact]
    public void ParsesTheStreamedArticleWithItsLeadAndDate()
    {
        var result = _parser.Parse(Fixture("article.html"));

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal("Örnek Tekstil işçileri üçüncü gününde: \"Haklarımızı alana kadar buradayız\"", result.Title);
        // The date after "metas", not the other story's listed before it.
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 9, 15, 0, TimeSpan.Zero), result.PublishedAt);
        Assert.Equal("https://example.org/uploads/2026/10/ornek-tekstil.png", result.ImageUrl);
        Assert.Equal(
        [
            "Ödenmeyen ücretleri için fabrika önünde bekleyen Örnek Tekstil işçileri, direnişin üçüncü gününde basın açıklaması yaptı.",
            "Gaziantep’te Örnek Tekstil’de çalışan 120 işçi, üç aydır ödenmeyen ücretleri için iş bıraktı.",
            "Bir işçi, \"Üç aydır maaş alamıyoruz\" dedi.",
            "Haklarımızı alana kadar buradayız.",
            "- Ücretlerin ödenmesi",
            "- Sendikal hakların tanınması",
            "Kaynak: ÖRNEK",
        ], result.Body!.Split("\n\n"));
    }

    [Fact]
    public void WithoutTheEmbeddedDateTheArticleStillParses()
    {
        var html = Fixture("article.html");
        html = html[..html.IndexOf("<script>", StringComparison.Ordinal)] + "</body></html>";

        var result = _parser.Parse(html);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Null(result.PublishedAt);
    }

    [Fact]
    public void APageWithoutTheArticleTextFails()
    {
        const string html = """
            <html><head><meta property="og:title" content="Bir sayfa"/></head>
            <body><div class="postContent"><h3 class="title">Bir sayfa</h3></div></body></html>
            """;

        Assert.Equal("No article body found", _parser.Parse(html).Failure);
        Assert.Null(_parser.Parse(html.Replace("</div></body>", "<div id=\"news\"><p>Metin</p></div></div></body>")).ImageUrl); // none named
        Assert.Equal("No title found", _parser.Parse("<html><body><p>Metin</p></body></html>").Failure);
    }
}
