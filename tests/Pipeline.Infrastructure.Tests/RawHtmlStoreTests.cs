namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class RawHtmlStoreTests(EmulatorFixture emulators)
{
    private readonly BlobRawHtmlStore _store = new(emulators.RawHtml);

    [Fact]
    public async Task SavesCompressedAndReadsBackUnchanged()
    {
        var articleId = Guid.NewGuid().ToString("N");
        var html = "<html><body><article><h1>İşçiler direnişte: ğüşıöç</h1>" + new string('x', 5000) + "</article></body></html>";

        var path = await _store.SaveAsync("evrensel", articleId, html, DateTimeOffset.UtcNow);

        Assert.StartsWith($"evrensel/{articleId}/", path);
        Assert.EndsWith(".html.gz", path);
        Assert.Equal(html, await _store.ReadAsync(path));

        var properties = await emulators.RawHtml.GetBlobClient(path).GetPropertiesAsync();
        Assert.Equal("gzip", properties.Value.ContentEncoding);
        Assert.True(properties.Value.ContentLength < html.Length, "stored size should be smaller than the page");
    }

    [Fact]
    public async Task EachFetchIsKeptAsSeparateFile()
    {
        var articleId = Guid.NewGuid().ToString("N");
        var first = await _store.SaveAsync("evrensel", articleId, "<p>v1</p>", DateTimeOffset.UtcNow);
        var second = await _store.SaveAsync("evrensel", articleId, "<p>v2</p>", DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.NotEqual(first, second);
        Assert.Equal("<p>v1</p>", await _store.ReadAsync(first));
        Assert.Equal("<p>v2</p>", await _store.ReadAsync(second));
    }
}
