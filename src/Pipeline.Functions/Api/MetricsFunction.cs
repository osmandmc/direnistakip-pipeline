using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Core.Articles;

namespace Pipeline.Functions.Api;

/// <summary><c>GET /api/metrics?days=14</c>: counts for the app's dashboard (D-015). Requires a function key (D-025).</summary>
public sealed class MetricsFunction(ArticleMetrics metrics)
{
    [Function("GetMetrics")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "metrics")] HttpRequest req,
        CancellationToken ct)
    {
        var days = ArticleMetrics.DefaultDays;
        if (req.Query["days"] is { Count: > 0 } text && (!int.TryParse(text, out days) || days < 1 || days > ArticleMetrics.MaxDays))
        {
            return new BadRequestObjectResult(new { error = $"'days' is a number from 1 to {ArticleMetrics.MaxDays}" });
        }
        return new OkObjectResult(await metrics.GetAsync(days, ct));
    }
}
