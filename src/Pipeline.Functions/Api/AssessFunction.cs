using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Core.Relevance;
using Pipeline.Core.Sources;

namespace Pipeline.Functions.Api;

/// <summary>
/// <c>POST /api/articles/assess?source=evrensel</c>: queues open articles without a relevance verdict for the
/// Relevance stage (B9), spaced for the model's rate limit. Requires a function key (D-025). Answers how many were queued.
/// </summary>
public sealed class AssessFunction(ArticleAssessing assessing)
{
    [Function("AssessArticles")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "articles/assess")] HttpRequest req,
        CancellationToken ct)
    {
        var source = req.Query["source"].FirstOrDefault();
        if (source is null || !SourceCatalog.All.Any(s => s.Id == source && s.Enabled))
        {
            return new BadRequestObjectResult(new { error = $"'source' is one of: {string.Join(", ", SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id))}" });
        }
        return new OkObjectResult(await assessing.RunAsync(source, ct));
    }
}
