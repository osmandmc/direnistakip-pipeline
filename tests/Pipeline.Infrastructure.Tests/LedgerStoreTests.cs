using Pipeline.Contracts;

namespace Pipeline.Infrastructure.Tests;

[Collection(EmulatorCollection.Name)]
public class LedgerStoreTests(EmulatorFixture emulators)
{
    private readonly CosmosLedgerStore _store = new(emulators.Database.GetContainer(StorageNames.LedgerContainer));

    private static LedgerRecord NewRecord(string id) => new(
        id, "evrensel", ArticleOrigin.Manual, "https://www.evrensel.net/haber/1/x",
        new Dictionary<Stage, StageResult> { [Stage.Submitted] = new(StageStatus.Succeeded, DateTimeOffset.UtcNow) });

    [Fact]
    public async Task CreatesOnlyOnce()
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.True(await _store.TryCreateAsync(NewRecord(id)));
        Assert.False(await _store.TryCreateAsync(NewRecord(id)));
    }

    [Fact]
    public async Task ReadsBackWhatWasCreated()
    {
        var id = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(NewRecord(id));

        var read = await _store.GetAsync(id);

        Assert.NotNull(read);
        Assert.Equal("evrensel", read.SourceId);
        Assert.Equal(ArticleOrigin.Manual, read.Origin);
        Assert.Equal(StageStatus.Succeeded, read.Stages[Stage.Submitted].Status);
    }

    [Fact]
    public async Task MissingArticleIsNull() =>
        Assert.Null(await _store.GetAsync(Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task RecordingAStageKeepsTheOthers()
    {
        var id = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(NewRecord(id));

        await _store.RecordStageAsync(id, Stage.Fetched, new(StageStatus.Succeeded, DateTimeOffset.UtcNow));
        await _store.RecordStageAsync(id, Stage.Parsed, new(StageStatus.Failed, DateTimeOffset.UtcNow, "evrensel-1", "No date found"));

        var read = await _store.GetAsync(id);
        Assert.NotNull(read);
        Assert.Equal(3, read.Stages.Count);
        Assert.Equal(StageStatus.Succeeded, read.Stages[Stage.Fetched].Status);
        Assert.Equal("No date found", read.Stages[Stage.Parsed].Reason);
    }

    [Fact]
    public async Task ConcurrentStageUpdatesAreBothKept()
    {
        var id = Guid.NewGuid().ToString("N");
        await _store.TryCreateAsync(NewRecord(id));

        await Task.WhenAll(
            _store.RecordStageAsync(id, Stage.Fetched, new(StageStatus.Succeeded, DateTimeOffset.UtcNow)),
            _store.RecordStageAsync(id, Stage.Parsed, new(StageStatus.Succeeded, DateTimeOffset.UtcNow)));

        var read = await _store.GetAsync(id);
        Assert.NotNull(read);
        Assert.Contains(Stage.Fetched, read.Stages.Keys);
        Assert.Contains(Stage.Parsed, read.Stages.Keys);
    }
}
