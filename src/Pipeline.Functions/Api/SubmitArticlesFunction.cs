using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Submission;

namespace Pipeline.Functions.Api;

/// <summary><c>POST /api/articles</c>: submit article URLs (manual or CSV). Requires a function key (D-025).</summary>
public sealed class SubmitArticlesFunction(ArticleSubmission submission)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("SubmitArticles")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "articles")] HttpRequest req,
        CancellationToken ct)
    {
        SubmitArticlesRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<SubmitArticlesRequest>(Json, ct);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid JSON: {ex.Message}" });
        }

        if (request?.Articles is not { Count: > 0 })
        {
            return new BadRequestObjectResult(new { error = "Send at least one article: { \"articles\": [ { \"url\": \"…\" } ] }" });
        }
        if (request.Articles.Count > ArticleSubmission.MaxBatchSize)
        {
            return new BadRequestObjectResult(new { error = $"At most {ArticleSubmission.MaxBatchSize} articles per request" });
        }

        return new OkObjectResult(await submission.SubmitAsync(request, ct));
    }
}
