using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Relevance assessments in the Cosmos "relevance" container, partitioned by ArticleId.</summary>
internal sealed class CosmosRelevanceStore(Container container) : IRelevanceStore
{
    public Task SaveAsync(RelevanceRecord record, CancellationToken ct = default) =>
        container.CreateItemAsync(
            record,
            new PartitionKey(record.ArticleId),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);
}
