using Pipeline.Contracts;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Collecting;

/// <summary>
/// One run over the feeds of the enabled sources (or one of them), shared by the timer and <c>POST /collect</c>.
/// A feed that cannot be read is reported with its reason and does not stop the others; the next run tries again.
/// </summary>
public sealed class FeedCollection(FeedCollector collector)
{
    public static IEnumerable<Source> FeedSources => SourceCatalog.All.Where(s => s.Enabled && s.FeedUrl is not null);

    /// <param name="sourceId">Only this source; null for all.</param>
    public async Task<IReadOnlyList<FeedRun>> RunAsync(string? sourceId = null, CancellationToken ct = default)
    {
        var runs = new List<FeedRun>();
        foreach (var source in FeedSources.Where(s => sourceId is null || s.Id == sourceId))
        {
            try
            {
                var result = await collector.CollectAsync(source, ct);
                runs.Add(new FeedRun(source.Id, result.Found, result.Accepted, result.Duplicate, result.Rejected, OutsideSection: result.OutsideSection));
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                runs.Add(new FeedRun(source.Id, 0, 0, 0, 0, ex.Message));
            }
        }
        return runs;
    }
}
