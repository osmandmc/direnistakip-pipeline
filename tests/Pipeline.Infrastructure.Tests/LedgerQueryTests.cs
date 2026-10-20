using Pipeline.Contracts;
using Pipeline.Core;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure.Tests;

/// <summary>The inbox list query against the Cosmos emulator. Each test uses its own source id to stay isolated.</summary>
[Collection(EmulatorCollection.Name)]
public class LedgerQueryTests(EmulatorFixture emulators)
{
    private readonly CosmosLedgerStore _store = new(emulators.Database.GetContainer(StorageNames.LedgerContainer));
    private readonly string _source = $"test-{Guid.NewGuid():N}";

    private static DateTimeOffset Oct(int day, int hour = 12) => new(2026, 10, day, hour, 0, 0, TurkeyTime.Offset);

    /// <summary>Creates a parsed article published at the given time; returns its id.</summary>
    private async Task<string> Parsed(DateTimeOffset publishedAt, string title = "Başlık")
    {
        var id = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(new LedgerRecord(
            id, _source, ArticleOrigin.Csv, $"https://www.evrensel.net/haber/{id}",
            new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow) },
            ListDate: TurkeyTime.Normalize(DateTimeOffset.UtcNow)));
        await _store.SetSummaryAsync(id, title, publishedAt);
        await _store.RecordStageAsync(id, Stage.Parsed, new(StageStatus.Succeeded, DateTimeOffset.UtcNow, "provided:csv"));
        return id;
    }

    private async Task<List<string>> AllIds(LedgerQuery query, int pageSize = 100)
    {
        var ids = new List<string>();
        string? next = null;
        do
        {
            var page = await _store.QueryAsync(query, pageSize, next);
            ids.AddRange(page.Items.Select(r => r.Id));
            next = page.Next;
        }
        while (next is not null);
        return ids;
    }

    [Fact]
    public async Task SummaryIsStoredInTurkeyTime()
    {
        // 19:12 UTC is 22:12 in Turkey.
        var id = await Parsed(new DateTimeOffset(2026, 10, 18, 19, 12, 0, TimeSpan.Zero), "Korteks'te iş kazası");

        var record = await _store.GetAsync(id);

        Assert.NotNull(record);
        Assert.Equal("Korteks'te iş kazası", record.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 22, 12, 0, TurkeyTime.Offset), record.PublishedAt);
        Assert.Equal(TurkeyTime.Offset, record.PublishedAt!.Value.Offset);
        Assert.Equal(record.PublishedAt, record.ListDate);
    }

    [Fact]
    public async Task FiltersByMonthAndSourceNewestFirst()
    {
        var early = await Parsed(Oct(2));
        var late = await Parsed(Oct(30));
        var middle = await Parsed(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero)); // given in UTC
        await Parsed(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TurkeyTime.Offset));         // September in Turkey
        await Parsed(new DateTimeOffset(2026, 10, 31, 21, 30, 0, TimeSpan.Zero));            // 1 November in Turkey

        var ids = await AllIds(new LedgerQuery(_source, TurkeyTime.StartOfDay(new(2026, 10, 1)), TurkeyTime.StartOfDay(new(2026, 11, 1))));

        Assert.Equal([late, middle, early], ids);
    }

    [Fact]
    public async Task PagesThroughEverythingOnceWithLoadMore()
    {
        var expected = new List<string>();
        for (var day = 1; day <= 7; day++)
        {
            expected.Insert(0, await Parsed(Oct(day)));
        }

        var ids = await AllIds(new LedgerQuery(_source), pageSize: 3);

        Assert.Equal(expected, ids);
    }

    [Fact]
    public async Task StatusFilterSeparatesReadyFromFailed()
    {
        var ready = await Parsed(Oct(5));
        var failed = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(new LedgerRecord(
            failed, _source, ArticleOrigin.Manual, "https://www.evrensel.net/haber/999999999/yok",
            new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow) },
            ListDate: TurkeyTime.Normalize(DateTimeOffset.UtcNow)));
        await _store.RecordStageAsync(failed, Stage.Fetched, new(StageStatus.Failed, DateTimeOffset.UtcNow, Reason: "HTTP 404"));

        Assert.Equal([ready], await AllIds(new LedgerQuery(_source, Status: ArticleStatusFilter.Parsed)));
        Assert.Equal([failed], await AllIds(new LedgerQuery(_source, Status: ArticleStatusFilter.Failed)));
        Assert.Equal(2, (await AllIds(new LedgerQuery(_source, Status: ArticleStatusFilter.All))).Count);
    }

    [Fact]
    public async Task OutcomeIsStoredWithoutTouchingStages()
    {
        var id = await Parsed(Oct(6));
        var at = new DateTimeOffset(2026, 11, 3, 7, 0, 0, TimeSpan.Zero);

        await _store.SetOutcomeAsync(id, new ArticleOutcome(OutcomeKind.Digested, at, ResistanceId: 412));

        var record = await _store.GetAsync(id);
        Assert.NotNull(record);
        Assert.Equal(new ArticleOutcome(OutcomeKind.Digested, at, 412), record.Outcome);
        Assert.Equal(TurkeyTime.Offset, record.Outcome!.At.Offset);
        Assert.Equal([Stage.Submitted, Stage.Parsed], record.Stages.Keys.Order());
    }

    [Fact]
    public async Task OutcomeFilterSeparatesInboxFromHandled()
    {
        var open = await Parsed(Oct(7));
        var digested = await Parsed(Oct(8));
        var dismissed = await Parsed(Oct(9));
        await _store.SetOutcomeAsync(digested, new ArticleOutcome(OutcomeKind.Digested, DateTimeOffset.UtcNow, ResistanceId: 1));
        await _store.SetOutcomeAsync(dismissed, new ArticleOutcome(OutcomeKind.Dismissed, DateTimeOffset.UtcNow, Reason: "İlgisiz"));

        Assert.Equal([open], await AllIds(new LedgerQuery(_source)));
        Assert.Equal([dismissed, digested], await AllIds(new LedgerQuery(_source, Outcome: OutcomeFilter.Handled)));
        Assert.Equal(3, (await AllIds(new LedgerQuery(_source, Outcome: OutcomeFilter.Any))).Count);
    }

    [Fact]
    public async Task InvalidNextIsRejectedClearly()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.QueryAsync(new LedgerQuery(_source), 10, "not*a*token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.QueryAsync(new LedgerQuery(_source), 10, "bm9uc2Vuc2U"));
    }

    [Fact]
    public async Task TitleSearchIgnoresCaseAndTurkishLetters()
    {
        var match = await Parsed(Oct(10), "Korteks’te İŞÇİLER greve çıktı");
        var other = await Parsed(Oct(11), "Belediye işçileri eylemde");
        await Parsed(Oct(12), "Hava durumu");

        Assert.Equal([other, match], await AllIds(new LedgerQuery(_source, TitleContains: "ISCILER")));
        Assert.Equal([match], await AllIds(new LedgerQuery(_source, TitleContains: "korteks'te")));
    }

    [Fact]
    public async Task TitleSearchFindsRecordsWithoutTheSearchField()
    {
        // A title written before searchTitle existed: matched on the title itself, case-insensitively.
        var id = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(new LedgerRecord(
            id, _source, ArticleOrigin.Csv, $"https://www.evrensel.net/haber/{id}",
            new Dictionary<Stage, StageResult> { [Stage.Parsed] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow) },
            Title: "Metal işçileri grevde", ListDate: TurkeyTime.Normalize(Oct(13))));

        Assert.Equal([id], await AllIds(new LedgerQuery(_source, TitleContains: "METAL")));
    }

    [Fact]
    public async Task CountsWithTheListsFilters()
    {
        await Parsed(Oct(14));
        var handled = await Parsed(Oct(15));
        await _store.SetOutcomeAsync(handled, new ArticleOutcome(OutcomeKind.Dismissed, DateTimeOffset.UtcNow));

        Assert.Equal(1, await _store.CountAsync(new LedgerQuery(_source)));
        Assert.Equal(2, await _store.CountAsync(new LedgerQuery(_source, Outcome: OutcomeFilter.Any)));
    }

    [Fact]
    public async Task ChangedSinceFindsRecentlyWrittenRecords()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var id = await Parsed(Oct(16));

        var records = await _store.ChangedSinceAsync(before);

        Assert.Contains(records, r => r.Id == id);
        Assert.DoesNotContain(await _store.ChangedSinceAsync(DateTimeOffset.UtcNow.AddHours(1)), r => r.Id == id);
    }
}
