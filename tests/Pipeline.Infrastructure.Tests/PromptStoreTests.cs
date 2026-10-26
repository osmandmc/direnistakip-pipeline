using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class PromptStoreTests(EmulatorFixture emulators)
{
    [Fact]
    public async Task VersionsAreKeptAndOneIsActiveUntilTheBuiltInOneIsBack()
    {
        var store = new BlobPromptStore(emulators.Prompts);
        var name = "test-" + Guid.NewGuid().ToString("N")[..8];
        var first = new PromptVersionRecord(name, "v-e1", "{{TITLE}} {{TEXT}}", "ilk", "ayse", new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero));
        var second = first with { Version = "v-e2", CreatedAt = first.CreatedAt.AddHours(1) };

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(first));
        Assert.Null(await store.GetActiveAsync(name));

        await store.SetActiveAsync(name, first with { ActivatedBy = "mehmet" });

        Assert.Equal(["v-e2", "v-e1"], (await store.ListAsync(name)).Select(v => v.Version));
        Assert.Equal(("v-e1", "mehmet"), ((await store.GetActiveAsync(name))!.Version, (await store.GetAsync(name, "v-e1"))!.ActivatedBy));

        await store.SetActiveAsync(name, null);
        Assert.Null(await store.GetActiveAsync(name));
    }
}
