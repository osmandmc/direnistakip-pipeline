using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>
/// The app's list values in the "lists" blob container (B16): every version as {updatedAt}.json, and latest.json, which
/// the Extract stage reads.
/// </summary>
internal sealed class BlobListStore(BlobContainerClient container) : IListStore
{
    private const string Latest = "latest.json";

    public async Task SaveAsync(ListsSnapshot lists, CancellationToken ct = default)
    {
        var json = BinaryData.FromObjectAsJson(lists, StorageClients.Json);
        await container.GetBlobClient($"{lists.UpdatedAt.UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}.json").UploadAsync(json, overwrite: false, ct);
        await container.GetBlobClient(Latest).UploadAsync(json, overwrite: true, ct);
    }

    public async Task<ListsSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            var content = await container.GetBlobClient(Latest).DownloadContentAsync(ct);
            return content.Value.Content.ToObjectFromJson<ListsSnapshot>(StorageClients.Json);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }
}
