using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class RelevanceStoreTests(EmulatorFixture emulators)
{
    private readonly Container _container = emulators.Database.GetContainer(StorageNames.RelevanceContainer);

    [Fact]
    public async Task EveryAssessmentIsKeptWithTheVerdictAsAName()
    {
        var articleId = Guid.NewGuid().ToString("N");
        var store = new CosmosRelevanceStore(_container);
        var at = DateTimeOffset.UtcNow;

        await store.SaveAsync(new RelevanceRecord("r1", articleId, "p1", RelevanceVerdict.NotRelevant, "Analiz yazısı.", "relevance-v1 · gemini", at));
        await store.SaveAsync(new RelevanceRecord("r2", articleId, "p1", RelevanceVerdict.Relevant, null, "relevance-v2 · gemini", at));

        using var query = _container.GetItemQueryIterator<string>(
            new QueryDefinition("SELECT VALUE c.verdict FROM c ORDER BY c.id"), requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(articleId) });
        var verdicts = new List<string>();
        while (query.HasMoreResults)
        {
            verdicts.AddRange(await query.ReadNextAsync());
        }
        Assert.Equal(["NotRelevant", "Relevant"], verdicts);
    }
}
