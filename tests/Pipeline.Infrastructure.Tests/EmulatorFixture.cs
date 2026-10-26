using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Azure.Cosmos;
using Testcontainers.Azurite;

namespace Pipeline.Infrastructure.Tests;

/// <summary>
/// Starts Azurite (blob, queue) and the Cosmos DB emulator in Docker once for all tests,
/// and creates the same containers infra/main.bicep creates in Azure.
/// </summary>
public sealed class EmulatorFixture : IAsyncLifetime
{
    // The emulator's fixed, publicly documented key; not a secret.
    private const string CosmosEmulatorKey =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private readonly AzuriteContainer _azurite = new AzuriteBuilder()
        .WithImage("mcr.microsoft.com/azure-storage/azurite:3.33.0")
        .Build();

    private readonly IContainer _cosmos = new ContainerBuilder()
        .WithImage("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview")
        .WithPortBinding(8081, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8081).ForPath("/")))
        .Build();

    public Database Database { get; private set; } = null!;
    public BlobContainerClient RawHtml { get; private set; } = null!;
    public BlobContainerClient Lists { get; private set; } = null!;
    public BlobContainerClient Prompts { get; private set; } = null!;
    public QueueServiceClient Queues { get; private set; } = null!;
    private CosmosClient? _cosmosClient;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_azurite.StartAsync(), _cosmos.StartAsync());

        var endpoint = $"http://{_cosmos.Hostname}:{_cosmos.GetMappedPublicPort(8081)}/";
        _cosmosClient = StorageClients.CreateCosmos(
            $"AccountEndpoint={endpoint};AccountKey={CosmosEmulatorKey}", null, new DefaultAzureCredential());
        Database = (await _cosmosClient.CreateDatabaseIfNotExistsAsync("pipeline-tests")).Database;
        await Database.CreateContainerIfNotExistsAsync(StorageNames.LedgerContainer, "/id");
        await Database.CreateContainerIfNotExistsAsync(StorageNames.ParsedContainer, "/articleId");
        await Database.CreateContainerIfNotExistsAsync(StorageNames.RelevanceContainer, "/articleId");
        await Database.CreateContainerIfNotExistsAsync(StorageNames.DraftsContainer, "/articleId");

        var storage = _azurite.GetConnectionString();
        RawHtml = new BlobServiceClient(storage).GetBlobContainerClient(StorageNames.RawHtmlBlobContainer);
        await RawHtml.CreateIfNotExistsAsync();
        Lists = new BlobServiceClient(storage).GetBlobContainerClient(StorageNames.ListsBlobContainer);
        await Lists.CreateIfNotExistsAsync();
        Prompts = new BlobServiceClient(storage).GetBlobContainerClient(StorageNames.PromptsBlobContainer);
        await Prompts.CreateIfNotExistsAsync();
        Queues = new QueueServiceClient(storage, StorageClients.QueueOptions);
    }

    public async Task DisposeAsync()
    {
        _cosmosClient?.Dispose();
        await _azurite.DisposeAsync();
        await _cosmos.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class EmulatorCollection : ICollectionFixture<EmulatorFixture>
{
    public const string Name = "emulators";
}
