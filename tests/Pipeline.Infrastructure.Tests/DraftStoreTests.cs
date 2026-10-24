using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class DraftStoreTests(EmulatorFixture emulators)
{
    private readonly CosmosDraftStore _store = new(emulators.Database.GetContainer(StorageNames.DraftsContainer));

    [Fact]
    public async Task TheLatestDraftWins()
    {
        var articleId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        await _store.SaveAsync(new DraftRecord("d1", articleId, "p1", "prompt-v2 · a", """{"relevant":false}""", false, null, now));
        await _store.SaveAsync(new DraftRecord("d2", articleId, "p1", "prompt-v2 · b", """{"relevant":true}""", true, "Örnek Metal · Fiili grev", now.AddMinutes(1)));

        var latest = await _store.GetLatestAsync(articleId);

        Assert.Equal(("prompt-v2 · b", true, "Örnek Metal · Fiili grev", """{"relevant":true}"""), (latest!.Version, latest.Relevant, latest.Summary, latest.Answer));
        Assert.Null(await _store.GetLatestAsync(Guid.NewGuid().ToString("N")));
    }
}
