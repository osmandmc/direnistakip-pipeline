using Azure;
using Azure.Storage.Blobs;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>
/// Prompt versions in the "prompts" blob container (B17): {name}/versions/{version}.json, and {name}/active.json with
/// the active version (absent: the built-in prompt).
/// </summary>
internal sealed class BlobPromptStore(BlobContainerClient container) : IPromptStore
{
    public async Task SaveAsync(PromptVersionRecord version, CancellationToken ct = default)
    {
        try
        {
            await container.GetBlobClient(VersionPath(version.Name, version.Version))
                .UploadAsync(BinaryData.FromObjectAsJson(version, StorageClients.Json), overwrite: false, ct);
        }
        catch (RequestFailedException e) when (e.Status == 409)
        {
            throw new InvalidOperationException($"Version {version.Version} exists already.", e);
        }
    }

    public Task<PromptVersionRecord?> GetAsync(string name, string version, CancellationToken ct = default) =>
        ReadAsync(VersionPath(name, version), ct);

    public async Task<IReadOnlyList<PromptVersionRecord>> ListAsync(string name, CancellationToken ct = default)
    {
        var versions = new List<PromptVersionRecord>();
        await foreach (var blob in container.GetBlobsAsync(prefix: $"{name}/versions/", cancellationToken: ct))
        {
            if (await ReadAsync(blob.Name, ct) is { } version)
                versions.Add(version);
        }
        return versions.OrderByDescending(v => v.CreatedAt).ToList();
    }

    public Task<PromptVersionRecord?> GetActiveAsync(string name, CancellationToken ct = default) => ReadAsync(ActivePath(name), ct);

    public async Task SetActiveAsync(string name, PromptVersionRecord? version, CancellationToken ct = default)
    {
        if (version is null)
        {
            await container.GetBlobClient(ActivePath(name)).DeleteIfExistsAsync(cancellationToken: ct);
            return;
        }
        var json = BinaryData.FromObjectAsJson(version, StorageClients.Json);
        await container.GetBlobClient(VersionPath(name, version.Version)).UploadAsync(json, overwrite: true, ct);
        await container.GetBlobClient(ActivePath(name)).UploadAsync(json, overwrite: true, ct);
    }

    private async Task<PromptVersionRecord?> ReadAsync(string path, CancellationToken ct)
    {
        try
        {
            var content = await container.GetBlobClient(path).DownloadContentAsync(ct);
            return content.Value.Content.ToObjectFromJson<PromptVersionRecord>(StorageClients.Json);
        }
        catch (RequestFailedException e) when (e.Status == 404)
        {
            return null;
        }
    }

    private static string VersionPath(string name, string version) => $"{name}/versions/{version}.json";

    private static string ActivePath(string name) => $"{name}/active.json";
}
