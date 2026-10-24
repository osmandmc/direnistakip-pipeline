using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Stages;

/// <summary>Runs for each message on <c>article-parsed</c>: the Relevance stage (B9, D-007).</summary>
public sealed class RelevanceFunction(RelevanceAssessment assessment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("Relevance")]
    public Task Run([QueueTrigger(QueueNames.ArticleParsed)] string message, CancellationToken ct)
    {
        var article = JsonSerializer.Deserialize<ArticleParsed>(message, Json)
            ?? throw new InvalidOperationException("Empty article-parsed message");
        return assessment.HandleAsync(article, ct);
    }
}
