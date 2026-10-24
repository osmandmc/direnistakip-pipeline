using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Stages;

/// <summary>Runs for each message on <c>article-relevant</c>: the Extract stage (B12).</summary>
public sealed class ExtractFunction(DraftExtraction extraction)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("Extract")]
    public Task Run([QueueTrigger(QueueNames.ArticleRelevant)] string message, CancellationToken ct)
    {
        var article = JsonSerializer.Deserialize<ArticleRelevant>(message, Json)
            ?? throw new InvalidOperationException("Empty article-relevant message");
        return extraction.HandleAsync(article, ct);
    }
}
