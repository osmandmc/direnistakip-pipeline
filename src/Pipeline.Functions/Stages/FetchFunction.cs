using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Fetching;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Stages;

/// <summary>
/// Runs for each message on <c>article-submitted</c>. Throwing (temporary failures) makes the host
/// retry after the visibility timeout; after <c>maxDequeueCount</c> attempts it goes to the poison queue.
/// </summary>
public sealed class FetchFunction(ArticleFetcher fetcher)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("Fetch")]
    public Task Run([QueueTrigger(QueueNames.ArticleSubmitted)] string message, FunctionContext context, CancellationToken ct)
    {
        var article = JsonSerializer.Deserialize<ArticleSubmitted>(message, Json)
            ?? throw new InvalidOperationException("Empty article-submitted message");
        return fetcher.FetchAsync(article, DequeueCount(context), ct);
    }

    // How many times this message has been delivered (1 = first attempt).
    private static long DequeueCount(FunctionContext context) =>
        context.BindingContext.BindingData.TryGetValue("DequeueCount", out var value) && value is not null
            ? Convert.ToInt64(value.ToString())
            : 1;
}
