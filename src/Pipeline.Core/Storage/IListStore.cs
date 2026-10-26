using Pipeline.Contracts;

namespace Pipeline.Core.Storage;

/// <summary>The app's list values (B16): every version kept, the latest read by the Extract stage.</summary>
public interface IListStore
{
    Task SaveAsync(ListsSnapshot lists, CancellationToken ct = default);

    /// <summary>The latest version, or null before the app first sent its lists.</summary>
    Task<ListsSnapshot?> GetLatestAsync(CancellationToken ct = default);
}
