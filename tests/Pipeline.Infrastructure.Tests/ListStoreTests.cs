using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class ListStoreTests(EmulatorFixture emulators)
{
    [Fact]
    public async Task TheLatestVersionIsReadBackAndEveryVersionKept()
    {
        var store = new BlobListStore(emulators.Lists);
        IReadOnlyDictionary<string, IReadOnlyList<string>> Lists(string reason) => new Dictionary<string, IReadOnlyList<string>> { ["reasons"] = ["TİS", reason] };
        var first = new ListsSnapshot(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), Lists("Düşük ücret"));
        var second = new ListsSnapshot(new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.Zero), Lists("Yeni neden"));

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        var latest = await store.GetLatestAsync();
        Assert.Equal((second.UpdatedAt, "Yeni neden"), (latest!.UpdatedAt, latest.Lists["reasons"][1]));
        var names = new List<string>();
        await foreach (var blob in emulators.Lists.GetBlobsAsync()) names.Add(blob.Name);
        Assert.Contains("20261026T100000000Z.json", names);
        Assert.Contains("20261026T110000000Z.json", names);
    }
}
