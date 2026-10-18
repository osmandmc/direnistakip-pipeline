using System.Net;
using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Ledger in the Cosmos "ledger" container; id and partition key are the ArticleId (D-020).</summary>
internal sealed class CosmosLedgerStore(Container container) : ILedgerStore
{
    private static readonly ItemRequestOptions NoContentBack = new() { EnableContentResponseOnWrite = false };

    public async Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default)
    {
        try
        {
            await container.CreateItemAsync(record, new PartitionKey(record.Id), NoContentBack, ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default)
    {
        try
        {
            var response = await container.ReadItemAsync<LedgerRecord>(articleId, new PartitionKey(articleId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // A patch changes only /stages/{stage}; other stages written meanwhile are kept.
    public Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default) =>
        container.PatchItemAsync<LedgerRecord>(
            articleId,
            new PartitionKey(articleId),
            [PatchOperation.Set($"/stages/{stage}", result)],
            new PatchItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);
}
