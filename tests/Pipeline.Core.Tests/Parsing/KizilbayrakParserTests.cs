using Pipeline.Core.Parsing;

namespace Pipeline.Core.Tests.Parsing;

public class KizilbayrakParserTests
{
    private readonly KizilbayrakParser _parser = new();

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kizilbayrak", "article.html"));

    [Fact]
    public void ParsesAPastedArticleWithItsLeadDateAndPicture()
    {
        var result = _parser.Parse(Fixture());

        Assert.True(result.Succeeded, result.Failure);
        Assert.Equal("Örnek Depo’da direniş sürüyor", result.Title);
        Assert.Equal(new DateTimeOffset(2026, 1, 7, 9, 5, 0, TurkeyTime.Offset), result.PublishedAt);
        Assert.Equal("https://kizilbayrak85.net/images/haber/ornek-depoda-direnis-suruyor.jpg", result.ImageUrl);
        Assert.Equal(
        [
            "Ücretleri ödenmeyen Örnek Depo işçileri direnişi sürdürüyor.",
            "Örnek Depo’da çalışan 80 işçi iş bıraktı.",
            "Haklarımızı alana kadar buradayız.",
            "Kızıl Bayrak / İstanbul",
        ], result.Body!.Split("\n\n"));
    }

    [Fact]
    public void WithoutATimeTheDayStartsAtMidnight()
    {
        var html = Fixture().Replace("<span id=\"ContentPlaceHolder1_lblHaberSaat\">09:05</span>", "");

        Assert.Equal(new DateTimeOffset(2026, 1, 7, 0, 0, 0, TurkeyTime.Offset), _parser.Parse(html).PublishedAt);
    }

    [Fact]
    public void APageWithoutTheArticleTextFails()
    {
        var html = Fixture();
        var start = html.IndexOf("<span id=\"ContentPlaceHolder1_lblHaberIcerik\">", StringComparison.Ordinal);
        var end = html.IndexOf("</span>\n  </div></div>", start, StringComparison.Ordinal) + "</span>".Length;

        Assert.Equal("No article body found", _parser.Parse(html[..start] + html[end..]).Failure);
        Assert.Equal("No title found", _parser.Parse("<html><body><p>Metin</p></body></html>").Failure);
    }
}
