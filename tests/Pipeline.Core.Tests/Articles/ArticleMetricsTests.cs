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
        Assert.Equal(new DailyMetrics(new(2026, 10, 18), "evrensel", 0, 0, 0, 0, 0), days[18]);
        Assert.Equal(new DailyMetrics(new(2026, 10, 19), "evrensel", 1, 0, 0, 0, 0), days[19]);
        Assert.Equal(new DailyMetrics(new(2026, 10, 20), "evrensel", 1, 1, 1, 1, 0), days[20]);
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
        Assert.Equal(["evrensel", "kizilbayrak"], result.Sources.Select(s => s.SourceId));
        Assert.All(result.Sources, s => Assert.Equal((null, 0), (s.LastReceivedAt, s.Received)));
        Assert.Equal(7 * 2, result.Days.Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
