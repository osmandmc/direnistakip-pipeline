using System.Text.Json;
using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class QueueSenderTests(EmulatorFixture emulators)
{
    [Fact]
    public async Task SendsJsonTheQueueTriggerCanRead()
    {
        var queue = emulators.Queues.GetQueueClient($"test-{Guid.NewGuid():N}");
        await queue.CreateAsync();
        var sent = new ArticleSubmitted(
            "abc", "https://www.evrensel.net/haber/1/x", "https://evrensel.net/haber/1/x",
            "evrensel", ArticleOrigin.Manual, DateTimeOffset.UtcNow, "Başlık");

        await new StorageQueueSender(emulators.Queues).SendAsync(queue.Name, sent);

        var received = (await queue.ReceiveMessageAsync()).Value;
        var json = received.Body.ToString(); // decoded from Base64 by the client options
        Assert.Contains("\"origin\":\"Manual\"", json);
        Assert.Equal(sent, JsonSerializer.Deserialize<ArticleSubmitted>(json, StorageClients.Json));
        Assert.True(received.ExpiresOn > DateTimeOffset.UtcNow.AddYears(1), "messages should not expire after the default 7 days");
    }

    [Fact]
    public async Task ALaterMessageStaysHiddenUntilItsTime()
    {
        var queue = emulators.Queues.GetQueueClient($"test-{Guid.NewGuid():N}");
        await queue.CreateAsync();

        await new StorageQueueSender(emulators.Queues).SendLaterAsync(queue.Name, new { articleId = "abc" }, TimeSpan.FromMinutes(5));

        Assert.Null((await queue.ReceiveMessageAsync()).Value);
        var peeked = (await queue.GetPropertiesAsync()).Value;
        Assert.Equal(1, peeked.ApproximateMessagesCount);
    }
}
