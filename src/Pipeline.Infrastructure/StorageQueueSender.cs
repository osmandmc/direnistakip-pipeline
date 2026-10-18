using Azure.Storage.Queues;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Sends JSON messages to Azure Storage queues (Base64, as the Functions queue trigger expects).</summary>
internal sealed class StorageQueueSender(QueueServiceClient queues) : IQueueSender
{
    public Task SendAsync<T>(string queueName, T message, CancellationToken ct = default) =>
        queues.GetQueueClient(queueName)
            .SendMessageAsync(BinaryData.FromObjectAsJson(message, StorageClients.Json), cancellationToken: ct);
}
