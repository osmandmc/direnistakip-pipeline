using Pipeline.Core.Prompts;
using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Relevance;

/// <summary>
/// Queues open articles of one source that have no relevance verdict yet (B9): those parsed before the stage existed,
/// or whose assessment failed. They go through the Relevance stage like new ones, spaced out so the model's per-minute
/// limit holds; the call itself returns at once. Newest first, at most <see cref="MaxPerRun"/> per call.
/// </summary>
public sealed class ArticleAssessing(ILedgerStore ledger, IParsedStore parsed, ILanguageModel model, IQueueSender queue, ActivePrompts? prompts = null)
{
    public const int MaxPerRun = 200;

    /// <summary>≈ 7 a minute: under the free tier's limit, with room for the articles the feeds bring meanwhile.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(9);

    private const int PageSize = 100;

    /// <param name="again">
    /// Instead of articles without a verdict: articles judged by an older version of the relevance prompt, judged again
    /// with the current one (B13).
    /// </param>
    public async Task<AssessResponse> RunAsync(string sourceId, bool again = false, CancellationToken ct = default)
    {
        if (!model.IsConfigured)
        {
            return new AssessResponse(0, (int)Spacing.TotalSeconds, false, false);
        }

        var current = prompts is null ? RelevancePrompt.Version : (await prompts.GetAsync(ActivePrompts.Relevance, ct)).Version;
        var query = new LedgerQuery(sourceId, Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open,
            RelevanceOlderThan: again ? current : null);
        var queued = 0;
        var more = false;
        string? next = null;
        do
        {
            var page = await ledger.QueryAsync(query, PageSize, next, ct);
            foreach (var record in page.Items.Where(r => (again ? RelevancePrompt.IsOlder(r, current) : !RelevanceAssessment.IsDone(r)) && r.Origin != ArticleOrigin.Manual))
            {
                if (queued == MaxPerRun)
                {
                    more = true;
                    break;
                }
                if (await parsed.GetLatestAsync(record.Id, ct) is not { } article)
                {
                    continue;
                }
                var message = new ArticleParsed(record.Id, record.SourceId, article.Id, article.ParserVersion, article.ParsedAt, Reassess: again);
                await queue.SendLaterAsync(QueueNames.ArticleParsed, message, Spacing * queued, ct);
                queued++;
            }
            next = page.Next;
        }
        while (next is not null && !more);

        return new AssessResponse(queued, (int)Spacing.TotalSeconds, more, true);
    }
}
