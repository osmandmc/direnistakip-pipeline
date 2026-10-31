using Pipeline.Contracts;
using Pipeline.Core.Articles;

namespace Pipeline.Core.Tests.Articles;

public class ArticleMetricsTests
{
    // "Now" is 20 October 2026, 10:00 in Turkey.
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TurkeyTime.Offset);

    private readonly InMemoryLedger _ledger = new();
    private readonly ArticleMetrics _metrics;

    public ArticleMetricsTests() => _metrics = new(_ledger, new FixedClock(Now));

    private static DateTimeOffset Oct(int day, int hour) => new(2026, 10, day, hour, 0, 0, TurkeyTime.Offset);

    private void Add(string id, string source, DateTimeOffset submitted, DateTimeOffset? parsed = null,
        (Stage Stage, DateTimeOffset At, string Reason)? failed = null, ArticleOutcome? outcome = null)
    {
        // Stage times are stored in UTC, as the stages write them.
        var stages = new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, submitted.ToUniversalTime()) };
        if (parsed is { } p) stages[Stage.Parsed] = new(StageStatus.Succeeded, p.ToUniversalTime());
        if (failed is { } f) stages[f.Stage] = new(StageStatus.Failed, f.At.ToUniversalTime(), Reason: f.Reason);
        _ledger.Records[id] = new LedgerRecord(id, source, ArticleOrigin.Collector, $"https://www.evrensel.net/haber/{id}", stages,
            outcome, Title: $"Haber {id}", ListDate: TurkeyTime.Normalize(submitted));
    }

    [Fact]
    public async Task CountsEachEventOnItsOwnTurkishDay()
    {
        // Received 19 Oct 23:30 (20:30 UTC, still the 19th in UTC too), read after midnight in Turkey (20 Oct 00:10 = 19 Oct 21:10 UTC).
        Add("a", "evrensel", Oct(19, 23).AddMinutes(30), parsed: Oct(20, 0).AddMinutes(10),
            outcome: new ArticleOutcome(OutcomeKind.Digested, Oct(20, 9), 412));
        Add("b", "evrensel", Oct(20, 8), failed: (Stage.Fetched, Oct(20, 8).AddMinutes(1), "HTTP 404"));
        Add("old", "evrensel", Oct(1, 12), parsed: Oct(1, 12)); // before a 3-day period

        var result = await _metrics.GetAsync(3);

        Assert.Equal((new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 20)), (result.From, result.To));
        var days = result.Days.Where(d => d.SourceId == "evrensel").ToDictionary(d => d.Day.Day);
        Assert.Equal([18, 19, 20], days.Keys.Order());
        Assert.Equal(new DailyMetrics(new(2026, 10, 18), "evrensel", 0, 0, 0, 0, 0), days[18] with { Now = null });
        Assert.Equal(new DailyMetrics(new(2026, 10, 19), "evrensel", 1, 0, 0, 0, 0), days[19] with { Now = null });
        Assert.Equal(new DailyMetrics(new(2026, 10, 20), "evrensel", 1, 1, 1, 1, 0), days[20] with { Now = null });
        // Where each day's arrivals stand now: "a" (received the 19th) is linked, "b" (the 20th) failed to download.
        Assert.Equal(new ArticleStates(1, 0, 0, 0, 0, 0, 0), days[19].Now);
        Assert.Equal(new ArticleStates(0, 0, 0, 0, 0, 1, 0), days[20].Now);
    }

    [Fact]
    public async Task SourcesShowLatestArrivalFailuresAndOpenInbox()
    {
        Add("a", "evrensel", Oct(19, 12), parsed: Oct(19, 12));                       // open in the inbox
        Add("b", "evrensel", Oct(20, 8), failed: (Stage.Fetched, Oct(20, 8), "HTTP 404"));
        Add("c", "evrensel", Oct(18, 12), parsed: Oct(18, 12),
            outcome: new ArticleOutcome(OutcomeKind.Dismissed, Oct(18, 13)));

        var result = await _metrics.GetAsync(14);

        var evrensel = result.Sources.Single(s => s.SourceId == "evrensel");
        Assert.Equal(("evrensel", 3, 1, 1), (evrensel.SourceId, evrensel.Received, evrensel.Failed, evrensel.Open));
        Assert.Equal(Oct(20, 8), evrensel.LastReceivedAt);
        Assert.Equal(ArticleOrigin.Collector, evrensel.LastReceivedOrigin);
        var failure = Assert.Single(result.RecentFailures);
        Assert.Equal(("b", Stage.Fetched, "HTTP 404"), (failure.ArticleId, failure.Stage, failure.Reason));
    }

    [Fact]
    public async Task ASilentSourceIsStillListed()
    {
        var result = await _metrics.GetAsync(7);

        // Every enabled source is listed, with a zero row per day.
        Assert.Equal(["evrensel", "kizilbayrak", "sendika"], result.Sources.Select(s => s.SourceId));
        Assert.All(result.Sources, s => Assert.Equal((null, 0), (s.LastReceivedAt, s.Received)));
        Assert.Equal(7 * 3, result.Days.Count);
    }

    private void Judge(string id, string verdict, DateTimeOffset at, bool drafted = false)
    {
        var record = _ledger.Records[id];
        var stages = new Dictionary<Stage, StageResult>(record.Stages)
        {
            [Stage.Relevance] = new(StageStatus.Succeeded, at.ToUniversalTime(), "relevance-v1", "…", verdict),
        };
        if (drafted) stages[Stage.Extracted] = new(StageStatus.Succeeded, at.ToUniversalTime(), "prompt-v2", "…", "Draft");
        _ledger.Records[id] = record with { Stages = stages };
    }

    [Fact]
    public async Task TheAisWorkIsCountedAndComparedWithTheResearchersDecisions()
    {
        Add("drafted", "evrensel", Oct(20, 8), parsed: Oct(20, 8));
        Judge("drafted", "Relevant", Oct(20, 8), drafted: true);
        Add("rejected", "evrensel", Oct(20, 8), parsed: Oct(20, 8));
        Judge("rejected", "NotRelevant", Oct(20, 8));
        Add("unassessed", "evrensel", Oct(20, 8), parsed: Oct(20, 8));
        Add("missed", "evrensel", Oct(19, 8), parsed: Oct(19, 8), outcome: new ArticleOutcome(OutcomeKind.Digested, Oct(19, 12), 7));
        Judge("missed", "NotRelevant", Oct(19, 9));
        Add("agreed", "evrensel", Oct(19, 8), parsed: Oct(19, 8), outcome: new ArticleOutcome(OutcomeKind.Dismissed, Oct(19, 12)));
        Judge("agreed", "NotRelevant", Oct(19, 9));

        var result = await new ArticleMetrics(_ledger, new FixedClock(Now), new Relevance.ScriptedModel(_ => throw new InvalidOperationException())).GetAsync(3);

        var evrensel = result.Sources.Single(s => s.SourceId == "evrensel");
        Assert.Equal((3, 1, 1, 1), (evrensel.Open, evrensel.OpenWithDraft, evrensel.OpenNotRelevant, evrensel.OpenNotAssessed));
        var today = result.Days.Single(d => d.SourceId == "evrensel" && d.Day.Day == 20);
        Assert.Equal((1, 0, 1, 1), (today.AssessedRelevant, today.AssessedUncertain, today.AssessedNotRelevant, today.Drafted));
        // Received the 20th: one with a draft, one rejected by the AI, one not assessed yet (to review).
        Assert.Equal(new ArticleStates(0, 0, 1, 1, 1, 0, 0), today.Now);
        Assert.True(result.Ai!.Enabled);
        Assert.Equal(new VerdictAgreement(RelevanceVerdict.NotRelevant, 1, 1), result.Ai.Agreement.Single(a => a.Verdict == RelevanceVerdict.NotRelevant));
        Assert.False((await _metrics.GetAsync(3)).Ai!.Enabled); // no model: the AI is off
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
