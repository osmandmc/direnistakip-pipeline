namespace Pipeline.Core.Parsing;

/// <summary>Turns one source's raw HTML into title, date and body. One implementation per source.</summary>
public interface IArticleParser
{
    string SourceId { get; }

    /// <summary>Stored on every parsed record (D-003); bump it when the rules change.</summary>
    string Version { get; }

    ParseResult Parse(string html);
}

/// <summary>Either the extracted fields, or the reason nothing usable was found.</summary>
public sealed record ParseResult(string? Title, DateTimeOffset? PublishedAt, string? Body, string? Failure)
{
    public bool Succeeded => Failure is null;

    public static ParseResult Success(string title, DateTimeOffset? publishedAt, string body) =>
        new(title, publishedAt, body, null);

    public static ParseResult Failed(string reason) => new(null, null, null, reason);
}
