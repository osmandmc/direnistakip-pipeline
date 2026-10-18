using Azure.Storage.Queues;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>
/// Sends JSON messages to Azure Storage queues (Base64, as the Functions queue trigger expects).
/// Messages never expire (the default is 7 days); failures end up in the poison queue instead.
/// </summary>
internal sealed class StorageQueueSender(QueueServiceClient queues) : IQueueSender
{
    private static readonly TimeSpan NeverExpires = TimeSpan.FromSeconds(-1);

    public Task SendAsync<T>(string queueName, T message, CancellationToken ct = default) =>
        queues.GetQueueClient(queueName)
            .SendMessageAsync(BinaryData.FromObjectAsJson(message, StorageClients.Json), timeToLive: NeverExpires, cancellationToken: ct);
}
