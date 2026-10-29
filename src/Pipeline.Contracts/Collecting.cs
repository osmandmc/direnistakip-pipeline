namespace Pipeline.Contracts;

/// <summary>What one feed brought in. Error: why the feed could not be read (then the counts are 0).</summary>
/// <param name="OutsideSection">Feed items outside the source's section, not taken (B18).</param>
public sealed record FeedRun(string SourceId, int Found, int Accepted, int Duplicate, int Rejected, string? Error = null, int OutsideSection = 0);

/// <summary><c>POST /collect</c>: one run over the feeds, as the timer does it.</summary>
public sealed record CollectResponse(IReadOnlyList<FeedRun> Feeds);
