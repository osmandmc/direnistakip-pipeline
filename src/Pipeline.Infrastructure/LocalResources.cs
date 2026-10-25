using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>
/// Creates the Cosmos database and containers, the queues and the blob container in the local emulators (F11), as
/// infra/main.bicep does in Azure. Called at startup only when running locally; existing ones are left as they are.
/// </summary>
public static class LocalResources
{
    /// <summary>Containers and partition keys; must match infra/main.bicep.</summary>
    private static readonly (string Name, string PartitionKey)[] Containers =
    [
        (StorageNames.LedgerContainer, "/id"),
        (StorageNames.ParsedContainer, "/articleId"),
        (StorageNames.RelevanceContainer, "/articleId"),
        (StorageNames.DraftsContainer, "/articleId"),
        ("rollups", "/id"),
    ];

    private static readonly string[] Queues =
        [QueueNames.ArticleSubmitted, QueueNames.ArticleFetched, QueueNames.ArticleParsed, QueueNames.ArticleRelevant, "article-extracted"];

    public static async Task EnsureAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        var database = (await services.GetRequiredService<CosmosClient>()
            .CreateDatabaseIfNotExistsAsync(config["Cosmos:Database"] ?? "pipeline", cancellationToken: ct)).Database;
        foreach (var (name, partitionKey) in Containers)
        {
            await database.CreateContainerIfNotExistsAsync(name, partitionKey, cancellationToken: ct);
        }

        var queues = services.GetRequiredService<QueueServiceClient>();
        foreach (var queue in Queues)
        {
            await queues.GetQueueClient(queue).CreateIfNotExistsAsync(cancellationToken: ct);
        }

        await services.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(StorageNames.RawHtmlBlobContainer)
            .CreateIfNotExistsAsync(cancellationToken: ct);
    }
}
