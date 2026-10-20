using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pipeline.Core.Collecting;
using Pipeline.Core.Sources;

namespace Pipeline.Functions.Collecting;

/// <summary>
/// Every two hours (at :05) reads the feed of each enabled source that has one and submits new articles.
/// Switched off unless the app setting <c>FeedCollection__Enabled</c> is true (infra parameter <c>feedCollectionEnabled</c>).
/// </summary>
public sealed class CollectFeedsFunction(FeedCollector collector, IConfiguration configuration, ILogger<CollectFeedsFunction> logger)
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
        foreach (var source in SourceCatalog.All.Where(s => s.Enabled && s.FeedUrl is not null))
        {
            try
            {
                var result = await collector.CollectAsync(source, ct);
                logger.LogInformation(
                    "Feed {Source}: {Found} found, {Accepted} new, {Duplicate} known, {Rejected} rejected",
                    result.SourceId, result.Found, result.Accepted, result.Duplicate, result.Rejected);
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                // One source's feed failing does not stop the others; the next run tries again.
                logger.LogWarning(ex, "Feed {Source} could not be read: {Message}", source.Id, ex.Message);
            }
        }
    }
}
