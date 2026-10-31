using Pipeline.Core.Collecting;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Tests.Collecting;

public class SectionPageReaderTests
{
    private readonly SectionPage _section = SourceCatalog.All.Single(s => s.Id == "sendika").SectionPage!;

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sendika", "emek-sermaye.html"));

    [Fact]
    public void TakesEachArticleOfTheMainListOnceWithItsTitle()
    {
        var articles = SectionPageReader.Read(Fixture(), _section);

        // Menu links, authors and pagination are left out; the picture and title links count once.
        Assert.Equal(
        [
            ("https://sendika.org/2026/10/ornek-tekstil-iscileri-ucuncu-gununde-754000", "Örnek Tekstil işçileri üçüncü gününde"),
            ("https://sendika.org/2026/10/kurye-iscileri-is-birakti-753999", "Kurye işçileri iş bıraktı"),
        ], articles.Select(a => (a.Url, a.Title)));
        Assert.All(articles, a => Assert.Null(a.PublishedAt));
    }

    [Fact]
    public void APageWithoutArticleLinksIsAFormatError()
    {
        var error = Assert.Throws<FormatException>(() => SectionPageReader.Read("<html><body><p>Bakımdayız</p></body></html>", _section));

        Assert.Contains("Emek-Sermaye", error.Message);
    }
}
