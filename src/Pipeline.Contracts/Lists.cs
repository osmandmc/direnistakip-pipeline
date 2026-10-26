namespace Pipeline.Contracts;

/// <summary>
/// The app's list values the extraction prompt offers (B16): list name ("reasons", "protestTypes", "cities" …) → values.
/// The app sends them when they change; the Extract stage uses the latest. UpdatedAt: when this version arrived.
/// </summary>
public sealed record ListsSnapshot(DateTimeOffset UpdatedAt, IReadOnlyDictionary<string, IReadOnlyList<string>> Lists);

/// <summary>Body of <c>PUT /lists</c>.</summary>
public sealed record ListsRequest(IReadOnlyDictionary<string, IReadOnlyList<string>> Lists);

/// <summary>
/// Answer of <c>PUT /lists</c> and <c>GET /lists</c>. Changed: a new version was stored (false when the lists were the same
/// as the latest). Counts: values per list.
/// </summary>
public sealed record ListsResponse(DateTimeOffset UpdatedAt, bool Changed, IReadOnlyDictionary<string, int> Counts);
