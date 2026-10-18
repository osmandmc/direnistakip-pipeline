using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>Parse results (D-003): saved as new records, never overwritten.</summary>
public interface IParsedStore
{
    Task SaveAsync(ParsedArticle parsed, CancellationToken ct = default);

    /// <summary>The most recently parsed record for the article, or null if it was never parsed.</summary>
    Task<ParsedArticle?> GetLatestAsync(string articleId, CancellationToken ct = default);
}
