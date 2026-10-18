using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Parsing;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Stages;

/// <summary>Runs for each message on <c>article-fetched</c>.</summary>
public sealed class ParseFunction(ArticleParsing parsing)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("Parse")]
    public Task Run([QueueTrigger(QueueNames.ArticleFetched)] string message, CancellationToken ct)
    {
        var article = JsonSerializer.Deserialize<ArticleFetched>(message, Json)
            ?? throw new InvalidOperationException("Empty article-fetched message");
        return parsing.ParseAsync(article, ct);
    }
}
