using Pipeline.Contracts;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Articles;

/// <summary>
/// Counts for the app's dashboard, made on request from the ledger (D-015 update): fine while the ledger holds
/// thousands of records; rollups can replace this behind the same response if it grows large.
/// </summary>
public sealed class ArticleMetrics(ILedgerStore ledger, TimeProvider clock)
{
    public const int DefaultDays = 14;
    public const int MaxDays = 31;
    public const int MaxFailures = 20;

    public async Task<MetricsResponse> GetAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, MaxDays);
        var to = Day(clock.GetUtcNow());
        var from = to.AddDays(1 - days);
        var records = await ledger.ChangedSinceAsync(TurkeyTime.StartOfDay(from), ct);

        bool InPeriod(DateTimeOffset at) => Day(at) >= from && Day(at) <= to;

        var sourceIds = SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id)
            .Concat(records.Select(r => r.SourceId))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        // One entry per event: (source, day, what happened).
        var events = records.SelectMany(r => Events(r).Where(e => InPeriod(e.At)).Select(e => (r.SourceId, Day: Day(e.At), e.Kind))).ToList();
        var dailyMetrics = sourceIds
            .SelectMany(source => Enumerable.Range(0, days).Select(i => from.AddDays(i)).Select(day =>
            {
                var today = events.Where(e => e.SourceId == source && e.Day == day).ToList();
                int Count(string kind) => today.Count(e => e.Kind == kind);
                return new DailyMetrics(day, source, Count(Received), Count(Parsed), Count(Failed), Count(Digested), Count(Dismissed));
            }))
            .ToList();

        var failures = records
            .SelectMany(r => r.Stages
                .Where(s => s.Value.Status == StageStatus.Failed && InPeriod(s.Value.At))
                .Select(s => new FailureSummary(r.Id, r.SourceId, r.Url, r.Title, s.Key, s.Value.At, s.Value.Reason)))
            .OrderByDescending(f => f.At)
            .ToList();

        var sources = new List<SourceMetrics>();
        foreach (var source in sourceIds)
        {
            var received = records
                .Where(r => r.SourceId == source && r.Stages.TryGetValue(Stage.Submitted, out var s) && InPeriod(s.At))
                .OrderByDescending(r => r.Stages[Stage.Submitted].At)
                .ToList();
            var open = await ledger.CountAsync(new LedgerQuery(source, Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open), ct);
            sources.Add(new SourceMetrics(
                source,
                received.FirstOrDefault()?.Stages[Stage.Submitted].At,
                received.FirstOrDefault()?.Origin,
                received.Count,
                failures.Where(f => f.SourceId == source).Select(f => f.ArticleId).Distinct().Count(),
                open));
        }

        return new MetricsResponse(from, to, sources, dailyMetrics, failures.Take(MaxFailures).ToList());
    }

    private const string Received = "received", Parsed = "parsed", Failed = "failed", Digested = "digested", Dismissed = "dismissed";

    private static IEnumerable<(DateTimeOffset At, string Kind)> Events(LedgerRecord record)
    {
        if (record.Stages.TryGetValue(Stage.Submitted, out var submitted))
        {
            yield return (submitted.At, Received);
        }
        if (record.Stages.TryGetValue(Stage.Parsed, out var parsed) && parsed.Status == StageStatus.Succeeded)
        {
            yield return (parsed.At, Parsed);
        }
        // An article counts as failed once, on the day of its latest failure.
        if (record.Stages.Values.Where(s => s.Status == StageStatus.Failed).MaxBy(s => s.At) is { } failed)
        {
            yield return (failed.At, Failed);
        }
        if (record.Outcome is { } outcome)
        {
            yield return (outcome.At, outcome.Kind == OutcomeKind.Digested ? Digested : Dismissed);
        }
    }

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(TurkeyTime.Offset).DateTime);
}
