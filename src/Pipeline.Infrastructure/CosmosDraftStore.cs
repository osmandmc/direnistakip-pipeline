using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>AI drafts in the Cosmos "drafts" container, partitioned by ArticleId.</summary>
internal sealed class CosmosDraftStore(Container container) : IDraftStore
{
    public Task SaveAsync(DraftRecord draft, CancellationToken ct = default) =>
        container.CreateItemAsync(
            draft,
            new PartitionKey(draft.ArticleId),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);

    // CreatedAt is stored as an ISO string; callers use UTC so the text order is the time order.
    public async Task<DraftRecord?> GetLatestAsync(string articleId, CancellationToken ct = default)
    {
        using var results = container.GetItemQueryIterator<DraftRecord>(
            new QueryDefinition("SELECT TOP 1 * FROM c ORDER BY c.createdAt DESC"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(articleId) });
        while (results.HasMoreResults)
        {
            var page = await results.ReadNextAsync(ct);
            if (page.Count > 0)
            {
                return page.First();
            }
        }
        return null;
    }
}
