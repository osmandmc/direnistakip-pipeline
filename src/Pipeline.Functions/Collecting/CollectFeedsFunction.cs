using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pipeline.Contracts;
using Pipeline.Core.Collecting;

namespace Pipeline.Functions.Collecting;

/// <summary>
/// Reads the feeds of the enabled sources and submits new articles: every two hours (at :05, UTC) when the app setting
/// <c>FeedCollection__Enabled</c> is true (infra parameter <c>feedCollectionEnabled</c>), and on request with
/// <c>POST /api/collect</c> (function key; works even when the timer is switched off).
/// </summary>
public sealed class CollectFeedsFunction(FeedCollection collection, IConfiguration configuration, ILogger<CollectFeedsFunction> logger)
{
    public const string EnabledSetting = "FeedCollection:Enabled";

    [Function("CollectFeeds")]
    public async Task Run([TimerTrigger("0 5 */2 * * *")] TimerInfo timer, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>(EnabledSetting))
        {
            logger.LogInformation("Feed collection is switched off ({Setting})", EnabledSetting);
            return;
        }
        Log(await collection.RunAsync(ct: ct), "timer");
    }

    /// <summary><c>POST /api/collect</c> or <c>POST /api/collect?source=evrensel</c>: collect now and report per feed.</summary>
    [Function("CollectNow")]
    public async Task<IActionResult> CollectNow(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "collect")] HttpRequest req,
        CancellationToken ct)
    {
        var source = req.Query["source"].FirstOrDefault();
        if (source is not null && !FeedCollection.FeedSources.Any(s => s.Id == source))
        {
            return new BadRequestObjectResult(new { error = $"'source' is one of: {string.Join(", ", FeedCollection.FeedSources.Select(s => s.Id))}" });
        }
        var runs = await collection.RunAsync(source, ct);
        Log(runs, "request");
        return new OkObjectResult(new CollectResponse(runs));
    }

    private void Log(IReadOnlyList<FeedRun> runs, string trigger)
    {
        foreach (var run in runs)
        {
            if (run.Error is null)
            {
                logger.LogInformation("Feed {Source} ({Trigger}): {Found} found, {Accepted} new, {Duplicate} known, {Rejected} rejected",
                    run.SourceId, trigger, run.Found, run.Accepted, run.Duplicate, run.Rejected);
            }
            else
            {
                logger.LogWarning("Feed {Source} ({Trigger}) could not be read: {Error}", run.SourceId, trigger, run.Error);
            }
        }
    }
}
