using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>AI drafts (D-003, B12): saved as new records, never overwritten.</summary>
public interface IDraftStore
{
    Task SaveAsync(DraftRecord draft, CancellationToken ct = default);

    /// <summary>The most recent draft of the article, or null.</summary>
    Task<DraftRecord?> GetLatestAsync(string articleId, CancellationToken ct = default);
}
