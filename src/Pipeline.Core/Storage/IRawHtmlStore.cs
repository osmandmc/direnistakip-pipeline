namespace Pipeline.Core.Storage;

/// <summary>Downloaded pages, kept as fetched (D-003). Each save is a new file; nothing is overwritten.</summary>
public interface IRawHtmlStore
{
    /// <summary>Saves the page and returns its path, which later stages use to read it back.</summary>
    Task<string> SaveAsync(string sourceId, string articleId, string html, DateTimeOffset fetchedAt, CancellationToken ct = default);

    Task<string> ReadAsync(string path, CancellationToken ct = default);

    /// <summary>The path of the article's most recently saved page, or null when none was saved (e.g. it came with its text).</summary>
    Task<string?> FindLatestAsync(string sourceId, string articleId, CancellationToken ct = default);
}
