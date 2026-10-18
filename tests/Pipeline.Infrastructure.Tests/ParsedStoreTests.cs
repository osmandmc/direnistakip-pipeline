using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class ParsedStoreTests(EmulatorFixture emulators)
{
    private readonly CosmosParsedStore _store = new(emulators.Database.GetContainer(StorageNames.ParsedContainer));

    private static ParsedArticle Parsed(string articleId, string version, DateTimeOffset at) => new(
        Guid.NewGuid().ToString("N"), articleId, "evrensel", version, at,
        $"evrensel/{articleId}/x.html.gz", "Başlık", new DateTimeOffset(2026, 10, 17, 22, 40, 0, TimeSpan.FromHours(3)), "Gövde metni");

    [Fact]
    public async Task LatestParseWins()
    {
        var articleId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await _store.SaveAsync(Parsed(articleId, "evrensel-1", now));
        await _store.SaveAsync(Parsed(articleId, "evrensel-2", now.AddMinutes(1)));

        var latest = await _store.GetLatestAsync(articleId);

        Assert.NotNull(latest);
        Assert.Equal("evrensel-2", latest.ParserVersion);
        Assert.Equal("Başlık", latest.Title);
        Assert.Equal(TimeSpan.FromHours(3), latest.PublishedAt?.Offset);
    }

    [Fact]
    public async Task NeverParsedIsNull() =>
        Assert.Null(await _store.GetLatestAsync(Guid.NewGuid().ToString("N")));
}
