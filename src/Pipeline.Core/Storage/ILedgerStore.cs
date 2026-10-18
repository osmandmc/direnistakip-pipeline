using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>One status record per article (D-015).</summary>
public interface ILedgerStore
{
    /// <summary>Creates the record unless one with the same id exists. Returns false if it already existed.</summary>
    Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default);

    Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default);

    /// <summary>Sets one stage's result without touching the others, so stages can update concurrently.</summary>
    Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default);
}
