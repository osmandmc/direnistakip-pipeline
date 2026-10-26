using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Extraction;

/// <summary>
/// Receives the app's list values (B16): checks every list the prompt offers is there and not empty, tidies the values
/// (trimmed, no duplicates, sorted) and stores a new version only when they changed.
/// </summary>
public sealed class ListsUpdate(IListStore store, TimeProvider clock)
{
    /// <exception cref="ArgumentException">A list is missing or empty.</exception>
    public async Task<ListsResponse> PutAsync(ListsRequest request, CancellationToken ct = default)
    {
        var lists = Tidy(request.Lists);
        var latest = await store.GetLatestAsync(ct);
        if (latest is not null && Same(latest.Lists, lists))
        {
            return new ListsResponse(latest.UpdatedAt, false, Counts(lists));
        }
        var snapshot = new ListsSnapshot(clock.GetUtcNow(), lists);
        await store.SaveAsync(snapshot, ct);
        return new ListsResponse(snapshot.UpdatedAt, true, Counts(lists));
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Tidy(IReadOnlyDictionary<string, IReadOnlyList<string>>? lists)
    {
        var tidy = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var key in ExtractionPrompt.ListKeys)
        {
            var values = lists is not null && lists.TryGetValue(key, out var v) ? v : null;
            var clean = (values ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (clean.Count == 0)
            {
                throw new ArgumentException($"The list '{key}' is missing or empty.");
            }
            tidy[key] = clean;
        }
        return tidy;
    }

    private static bool Same(IReadOnlyDictionary<string, IReadOnlyList<string>> a, IReadOnlyDictionary<string, IReadOnlyList<string>> b) =>
        ExtractionPrompt.ListKeys.All(k => a.TryGetValue(k, out var x) && b.TryGetValue(k, out var y) && x.SequenceEqual(y, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, int> Counts(IReadOnlyDictionary<string, IReadOnlyList<string>> lists) =>
        lists.ToDictionary(l => l.Key, l => l.Value.Count);
}
