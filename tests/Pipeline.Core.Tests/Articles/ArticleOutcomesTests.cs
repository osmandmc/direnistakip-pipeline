using Pipeline.Contracts;
using Pipeline.Core.Articles;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Tests.Articles;

public class ArticleOutcomesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TurkeyTime.Offset);

    private readonly InMemoryLedger _ledger = new();
    private readonly InMemoryParsed _parsed = new();
    private readonly ClockAt _clock = new(Now);
    private readonly ArticleOutcomes _outcomes;

    public ArticleOutcomesTests()
    {
        _outcomes = new(_ledger, _clock);
        foreach (var id in new[] { "a1", "a2" })
        {
            _ledger.Records[id] = new LedgerRecord(
                id, "evrensel", ArticleOrigin.Csv, $"https://www.evrensel.net/haber/{id}",
                new Dictionary<Stage, StageResult>
                {
                    [Stage.Submitted] = new(StageStatus.Succeeded, Now.AddDays(-1)),
                    [Stage.Parsed] = new(StageStatus.Succeeded, Now.AddDays(-1), "provided:csv"),
                },
                ListDate: Now.AddDays(-1));
        }
    }

    [Fact]
    public async Task DigestedIsStoredWithTheCallTime()
    {
        var outcome = await _outcomes.RecordAsync("a1", new(OutcomeKind.Digested, ResistanceId: 412));

        Assert.Equal(new ArticleOutcome(OutcomeKind.Digested, Now, 412), outcome);
        Assert.Equal(outcome, _ledger.Records["a1"].Outcome);
        Assert.Equal(2, _ledger.Records["a1"].Stages.Count); // stages untouched
    }

    [Fact]
    public async Task DismissedKeepsTheReasonAndTheGivenTime()
    {
        var at = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

        var outcome = await _outcomes.RecordAsync("a1", new(OutcomeKind.Dismissed, at, Reason: "  İlgisiz "));

        Assert.Equal(new ArticleOutcome(OutcomeKind.Dismissed, TurkeyTime.Normalize(at), Reason: "İlgisiz"), outcome);
    }

    [Fact]
    public async Task RepeatingTheSameCallChangesNothing()
    {
        var first = await _outcomes.RecordAsync("a1", new(OutcomeKind.Digested, ResistanceId: 412));
        _clock.Now = Now.AddHours(2);

        var second = await _outcomes.RecordAsync("a1", new(OutcomeKind.Digested, ResistanceId: 412));

        Assert.Equal(first, second);
        Assert.Equal(Now, _ledger.Records["a1"].Outcome!.At);
    }

    [Fact]
    public async Task TheLatestDecisionWins()
    {
        await _outcomes.RecordAsync("a1", new(OutcomeKind.Dismissed));
        _clock.Now = Now.AddHours(2);

        await _outcomes.RecordAsync("a1", new(OutcomeKind.Digested, ResistanceId: 7));

        Assert.Equal(new ArticleOutcome(OutcomeKind.Digested, Now.AddHours(2), 7), _ledger.Records["a1"].Outcome);
    }

    [Fact]
    public async Task UnknownArticleIsNull() =>
        Assert.Null(await _outcomes.RecordAsync("nope", new(OutcomeKind.Dismissed)));

    [Theory]
    [InlineData(null, null)]                    // no kind
    [InlineData(OutcomeKind.Digested, null)]    // digested without a resistance
    [InlineData(OutcomeKind.Digested, 0)]
    [InlineData(OutcomeKind.Dismissed, 5)]      // resistance id on a dismissal
    public async Task InvalidRequestsAreRejected(OutcomeKind? kind, int? resistanceId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _outcomes.RecordAsync("a1", new(kind, ResistanceId: resistanceId)));
        Assert.Null(_ledger.Records["a1"].Outcome);
    }

    [Fact]
    public async Task TooLongReasonIsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _outcomes.RecordAsync("a1", new(OutcomeKind.Dismissed, Reason: new string('x', ArticleOutcomes.MaxReasonLength + 1))));

    [Fact]
    public async Task InboxHidesHandledArticlesAndShowsTheOutcome()
    {
        var queries = new ArticleQueries(_ledger, _parsed);
        await _outcomes.RecordAsync("a1", new(OutcomeKind.Dismissed));

        var open = await queries.ListAsync(new LedgerQuery(), 20, null);
        var handled = await queries.ListAsync(new LedgerQuery(Outcome: OutcomeFilter.Handled), 20, null);
        var any = await queries.ListAsync(new LedgerQuery(Outcome: OutcomeFilter.Any), 20, null);

        Assert.Equal(["a2"], open.Items.Select(i => i.ArticleId));
        Assert.Equal(["a1"], handled.Items.Select(i => i.ArticleId));
        Assert.Equal(OutcomeKind.Dismissed, handled.Items[0].Outcome!.Kind);
        Assert.Equal(2, any.Items.Count);
        Assert.Equal(OutcomeKind.Dismissed, (await queries.GetAsync("a1"))!.Outcome!.Kind);
    }

    private sealed class ClockAt(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }
}
