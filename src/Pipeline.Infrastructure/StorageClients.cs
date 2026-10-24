using System.Text.Json;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Cosmos;

namespace Pipeline.Infrastructure;

/// <summary>Names that must match infra/main.bicep.</summary>
internal static class StorageNames
{
    public const string LedgerContainer = "ledger";
    public const string ParsedContainer = "parsed";
    public const string RelevanceContainer = "relevance";
    public const string DraftsContainer = "drafts";
    public const string RawHtmlBlobContainer = "raw-html";
}

/// <summary>Creates Azure clients. In Azure: managed identity. Locally and in tests: connection strings (emulators).</summary>
internal static class StorageClients
{
    /// <summary>JSON shape of everything we store or queue: camelCase properties, enums as names.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The Functions queue trigger expects Base64-encoded messages by default.
    public static readonly QueueClientOptions QueueOptions = new() { MessageEncoding = QueueMessageEncoding.Base64 };

    public static CosmosClient CreateCosmos(string? connectionString, string? endpoint, TokenCredential credential)
    {
        var options = new CosmosClientOptions { UseSystemTextJsonSerializerWithOptions = Json };
        if (!string.IsNullOrEmpty(connectionString))
        {
            // Emulator: talk only to the given address over the gateway.
            options.ConnectionMode = ConnectionMode.Gateway;
            options.LimitToEndpoint = true;
            return new CosmosClient(connectionString, options);
        }
        return new CosmosClient(endpoint ?? throw new InvalidOperationException("Cosmos:Endpoint is not set."), credential, options);
    }

    public static BlobServiceClient CreateBlobs(string? connectionString, string? accountName, TokenCredential credential) =>
        string.IsNullOrEmpty(accountName)
            ? new BlobServiceClient(connectionString)
            : new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), credential);

    public static QueueServiceClient CreateQueues(string? connectionString, string? accountName, TokenCredential credential) =>
        string.IsNullOrEmpty(accountName)
            ? new QueueServiceClient(connectionString, QueueOptions)
            : new QueueServiceClient(new Uri($"https://{accountName}.queue.core.windows.net"), credential, QueueOptions);
}
