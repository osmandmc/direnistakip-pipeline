using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Api;

/// <summary>
/// AI drafts (B12). Requires a function key (D-025).
/// <c>GET /api/articles/{id}/draft</c>: the article's latest draft (404 when none).
/// <c>POST /api/articles/draft?source=evrensel</c>: queues open articles judged relevant or uncertain without a draft.
/// </summary>
public sealed class DraftFunction(IDraftStore drafts, ArticleDrafting drafting)
{
    [Function("GetDraft")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "articles/{id}/draft")] HttpRequest req,
        string id,
        CancellationToken ct) =>
        await drafts.GetLatestAsync(id, ct) is { } d
            ? new OkObjectResult(new DraftResponse(d.ArticleId, d.Version, d.Answer, d.Relevant, d.Summary, d.CreatedAt))
            : new NotFoundObjectResult(new { error = "No draft for this article." });

    [Function("QueueDrafts")]
    public async Task<IActionResult> Queue(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "articles/draft")] HttpRequest req,
        CancellationToken ct)
    {
        var source = req.Query["source"].FirstOrDefault();
        if (source is null || !SourceCatalog.All.Any(s => s.Id == source && s.Enabled))
        {
            return new BadRequestObjectResult(new { error = $"'source' is one of: {string.Join(", ", SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id))}" });
        }
        return new OkObjectResult(await drafting.RunAsync(source, ct));
    }
}
