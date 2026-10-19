using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Articles;

namespace Pipeline.Functions.Api;

/// <summary>
/// <c>POST /api/articles/{id}/outcome</c>: the app reports that an article was digested or dismissed (D-011).
/// Requires a function key (D-025). Safe to repeat.
/// </summary>
public sealed class ArticleOutcomeFunction(ArticleOutcomes outcomes)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("RecordArticleOutcome")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "articles/{id}/outcome")] HttpRequest req,
        string id,
        CancellationToken ct)
    {
        RecordOutcomeRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<RecordOutcomeRequest>(Json, ct);
        }
        catch (JsonException ex)
        {
            return new BadRequestObjectResult(new { error = $"Invalid JSON: {ex.Message}" });
        }
        if (request is null)
        {
            return new BadRequestObjectResult(new { error = "Send { \"kind\": \"Digested\", \"resistanceId\": 1 } or { \"kind\": \"Dismissed\" }" });
        }

        try
        {
            return await outcomes.RecordAsync(id, request, ct) is { } outcome
                ? new OkObjectResult(outcome)
                : new NotFoundObjectResult(new { error = $"No article with id '{id}'" });
        }
        catch (ArgumentException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }
    }
}
