using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

public static class StorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the storage interfaces. Settings (app settings in Azure, local.settings.json locally):
    /// <c>Cosmos:Endpoint</c> + <c>Cosmos:Database</c> in Azure, or <c>Cosmos:ConnectionString</c> for the emulator;
    /// <c>AzureWebJobsStorage:accountName</c> in Azure, or the <c>AzureWebJobsStorage</c> connection string locally.
    /// </summary>
    public static IServiceCollection AddPipelineStorage(this IServiceCollection services, IConfiguration config)
    {
        var storageConnection = config["AzureWebJobsStorage"];
        var storageAccount = config["AzureWebJobsStorage:accountName"];

        services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton(sp => StorageClients.CreateCosmos(
            config["Cosmos:ConnectionString"], config["Cosmos:Endpoint"], sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton(sp => StorageClients.CreateBlobs(
            storageConnection, storageAccount, sp.GetRequiredService<TokenCredential>()));
        services.AddSingleton(sp => StorageClients.CreateQueues(
            storageConnection, storageAccount, sp.GetRequiredService<TokenCredential>()));

        Database Db(IServiceProvider sp) =>
            sp.GetRequiredService<CosmosClient>().GetDatabase(config["Cosmos:Database"] ?? "pipeline");

        services.AddSingleton<ILedgerStore>(sp => new CosmosLedgerStore(Db(sp).GetContainer(StorageNames.LedgerContainer)));
        services.AddSingleton<IParsedStore>(sp => new CosmosParsedStore(Db(sp).GetContainer(StorageNames.ParsedContainer)));
        services.AddSingleton<IRelevanceStore>(sp => new CosmosRelevanceStore(Db(sp).GetContainer(StorageNames.RelevanceContainer)));
        services.AddSingleton<IRawHtmlStore>(sp => new BlobRawHtmlStore(
            sp.GetRequiredService<BlobServiceClient>().GetBlobContainerClient(StorageNames.RawHtmlBlobContainer)));
        services.AddSingleton<IQueueSender>(sp => new StorageQueueSender(sp.GetRequiredService<QueueServiceClient>()));
        return services;
    }
}
