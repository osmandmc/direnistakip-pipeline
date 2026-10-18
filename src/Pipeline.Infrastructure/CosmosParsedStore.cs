using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Parse results in the Cosmos "parsed" container, partitioned by ArticleId.</summary>
internal sealed class CosmosParsedStore(Container container) : IParsedStore
{
    public Task SaveAsync(ParsedArticle parsed, CancellationToken ct = default) =>
        container.CreateItemAsync(
            parsed,
            new PartitionKey(parsed.ArticleId),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);

    // ParsedAt is stored as an ISO string; callers use UTC so the text order is the time order.
    public async Task<ParsedArticle?> GetLatestAsync(string articleId, CancellationToken ct = default)
    {
        using var results = container.GetItemQueryIterator<ParsedArticle>(
            new QueryDefinition("SELECT TOP 1 * FROM c ORDER BY c.parsedAt DESC"),
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
