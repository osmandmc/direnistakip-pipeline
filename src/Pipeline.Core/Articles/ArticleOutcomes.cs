using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Articles;

/// <summary>
/// Records what the researchers decided about an article (D-011): digested into a resistance, or dismissed.
/// Repeating a call is harmless, and the latest decision wins.
/// </summary>
public sealed class ArticleOutcomes(ILedgerStore ledger, TimeProvider time)
{
    public const int MaxReasonLength = 500;

    /// <summary>
    /// Returns the stored outcome, or null when there is no such article.
    /// Throws <see cref="ArgumentException"/> when the request is invalid.
    /// </summary>
    public async Task<ArticleOutcome?> RecordAsync(string articleId, RecordOutcomeRequest request, CancellationToken ct = default)
    {
        var outcome = Validate(request);
        var record = await ledger.GetAsync(articleId, ct);
        if (record is null)
        {
            return null;
        }

        // A retry of the same decision keeps the original time.
        if (record.Outcome is { } current
            && current.Kind == outcome.Kind
            && current.ResistanceId == outcome.ResistanceId
            && current.Reason == outcome.Reason
            && (request.At is null || current.At == TurkeyTime.Normalize(request.At.Value)))
        {
            return current;
        }

        var stored = outcome with { At = TurkeyTime.Normalize(request.At ?? time.GetUtcNow()) };
        await ledger.SetOutcomeAsync(articleId, stored, ct);
        return stored;
    }

    private static ArticleOutcome Validate(RecordOutcomeRequest request)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > MaxReasonLength })
        {
            throw new ArgumentException($"'reason' is at most {MaxReasonLength} characters");
        }
        return request.Kind switch
        {
            null => throw new ArgumentException("'kind' is one of: Digested, Dismissed"),
            OutcomeKind.Digested when request.ResistanceId is not > 0 =>
                throw new ArgumentException("'Digested' needs a 'resistanceId'"),
            OutcomeKind.Dismissed when request.ResistanceId is not null =>
                throw new ArgumentException("'resistanceId' is only for 'Digested'"),
            { } kind => new ArticleOutcome(kind, default, request.ResistanceId, reason),
        };
    }
}
