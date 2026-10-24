using Pipeline.Contracts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Sources;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Articles;

/// <summary>
/// Counts for the app's dashboard, made on request from the ledger (D-015 update): fine while the ledger holds
/// thousands of records; rollups can replace this behind the same response if it grows large.
/// </summary>
public sealed class ArticleMetrics(ILedgerStore ledger, TimeProvider clock, ILanguageModel? model = null)
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
                return new DailyMetrics(day, source, Count(Received), Count(Parsed), Count(Failed), Count(Digested), Count(Dismissed),
                    Count(AssessedRelevant), Count(AssessedUncertain), Count(AssessedNotRelevant), Count(Drafted));
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
            var openQuery = new LedgerQuery(source, Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open);
            var open = await ledger.CountAsync(openQuery, ct);
            sources.Add(new SourceMetrics(
                source,
                received.FirstOrDefault()?.Stages[Stage.Submitted].At,
                received.FirstOrDefault()?.Origin,
                received.Count,
                failures.Where(f => f.SourceId == source).Select(f => f.ArticleId).Distinct().Count(),
                open,
                await ledger.CountAsync(openQuery with { Draft = DraftFilter.Ready }, ct),
                await ledger.CountAsync(openQuery with { Relevance = RelevanceFilter.NotRelevant }, ct),
                await ledger.CountAsync(openQuery with { Relevance = RelevanceFilter.NotAssessed }, ct)));
        }

        // What the AI said about the articles researchers decided in the period, against what they did.
        var agreement = Enum.GetValues<RelevanceVerdict>().Select(verdict =>
        {
            var decided = records.Where(r => r.Outcome is { } o && InPeriod(o.At) && Verdict(r) == verdict).ToList();
            return new VerdictAgreement(verdict, decided.Count(r => r.Outcome!.Kind == OutcomeKind.Digested), decided.Count(r => r.Outcome!.Kind == OutcomeKind.Dismissed));
        }).ToList();

        return new MetricsResponse(from, to, sources, dailyMetrics, failures.Take(MaxFailures).ToList(),
            new AiMetrics(model?.IsConfigured ?? false, agreement));
    }

    private const string Received = "received", Parsed = "parsed", Failed = "failed", Digested = "digested", Dismissed = "dismissed",
        AssessedRelevant = "relevant", AssessedUncertain = "uncertain", AssessedNotRelevant = "notRelevant", Drafted = "drafted";

    private static RelevanceVerdict? Verdict(LedgerRecord record) =>
        record.Stages.TryGetValue(Stage.Relevance, out var stage) && stage.Status == StageStatus.Succeeded
            && Enum.TryParse<RelevanceVerdict>(stage.Answer, out var verdict) ? verdict : null;

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
        if (Verdict(record) is { } verdict)
        {
            yield return (record.Stages[Stage.Relevance].At, verdict switch
            {
                RelevanceVerdict.Relevant => AssessedRelevant,
                RelevanceVerdict.Uncertain => AssessedUncertain,
                _ => AssessedNotRelevant,
            });
        }
        if (record.Stages.TryGetValue(Stage.Extracted, out var extracted) && extracted.Answer == Extraction.ExtractionAnswers.Draft)
        {
            yield return (extracted.At, Drafted);
        }
    }

    private static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(TurkeyTime.Offset).DateTime);
}
