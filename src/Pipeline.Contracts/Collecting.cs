namespace Pipeline.Contracts;

/// <summary>What one feed brought in. Error: why the feed could not be read (then the counts are 0).</summary>
public sealed record FeedRun(string SourceId, int Found, int Accepted, int Duplicate, int Rejected, string? Error = null);

/// <summary><c>POST /collect</c>: one run over the feeds, as the timer does it.</summary>
public sealed record CollectResponse(IReadOnlyList<FeedRun> Feeds);
