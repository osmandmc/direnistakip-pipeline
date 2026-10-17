using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<OutcomeKind>))]
public enum OutcomeKind
{
    Digested,
    Dismissed,
}

/// <summary>Body of <c>POST /articles/{id}/outcome</c> (D-011). Safe to repeat.</summary>
public sealed record ArticleOutcome(
    OutcomeKind Kind,
    DateTimeOffset At,
    int? ResistanceId = null,
    string? Reason = null);
