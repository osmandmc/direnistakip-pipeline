using System.Text.Json.Serialization;

namespace Pipeline.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<OutcomeKind>))]
public enum OutcomeKind
{
    Digested,
    Dismissed,
}

/// <summary>What the researchers decided about an article, kept on its ledger record (D-011). The latest decision wins.</summary>
public sealed record ArticleOutcome(
    OutcomeKind Kind,
    DateTimeOffset At,
    int? ResistanceId = null,
    string? Reason = null);

/// <summary>
/// Body of <c>POST /articles/{id}/outcome</c> (D-011). Safe to repeat.
/// <c>Digested</c> needs <see cref="ResistanceId"/>; <c>Dismissed</c> may give a <see cref="Reason"/>.
/// <see cref="At"/> defaults to the time of the call.
/// </summary>
public sealed record RecordOutcomeRequest(
    OutcomeKind? Kind,
    DateTimeOffset? At = null,
    int? ResistanceId = null,
    string? Reason = null);
