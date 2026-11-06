using Pipeline.Contracts;
using Pipeline.Core.Prompts;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Extraction;

/// <summary>
/// Queues open articles of one source that were judged relevant or uncertain but have no draft yet (B12): those
/// assessed before the Extract stage existed, or whose drafting failed. Spaced for the model's per-minute limit; newest
/// first, at most <see cref="MaxPerRun"/> per call.
/// </summary>
public sealed class ArticleDrafting(ILedgerStore ledger, ILanguageModel model, IQueueSender queue, ActivePrompts? prompts = null)
{
    public const int MaxPerRun = 100;

    /// <summary>≈ 6 a minute: well under the free tier's per-minute limit, with room for new articles and the Relevance stage.</summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromSeconds(10);

    private const int PageSize = 100;

    /// <param name="again">
    /// Instead of articles without a draft: articles drafted by an older version of the extraction prompt, drafted again
    /// with the current one (D-029). Earlier drafts are kept; the app shows the latest.
    /// </param>
    public async Task<DraftQueueResponse> RunAsync(string sourceId, bool again = false, CancellationToken ct = default)
    {
        if (!model.IsConfigured)
        {
            return new DraftQueueResponse(0, (int)Spacing.TotalSeconds, false, false);
        }

        var current = prompts is null ? ExtractionPrompt.Version : (await prompts.GetAsync(ActivePrompts.Extraction, ct)).Version;
        var query = new LedgerQuery(sourceId, Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open, Relevance: RelevanceFilter.Other);
        var queued = 0;
        var more = false;
        string? next = null;
        do
        {
            var page = await ledger.QueryAsync(query, PageSize, next, ct);
            foreach (var record in page.Items.Where(r => again ? ExtractionPrompt.IsOlder(r, current) : !DraftExtraction.IsDone(r)))
            {
                if (!record.Stages.TryGetValue(Stage.Relevance, out var relevance)
                    || !Enum.TryParse<RelevanceVerdict>(relevance.Answer, out var verdict) || verdict == RelevanceVerdict.NotRelevant)
                {
                    continue; // not assessed yet: the Relevance stage sends it on when it is
                }
                if (queued == MaxPerRun)
                {
                    more = true;
                    break;
                }
                await queue.SendLaterAsync(QueueNames.ArticleRelevant, new ArticleRelevant(record.Id, record.SourceId, verdict, relevance.At, Redraft: again), Spacing * queued, ct);
                queued++;
            }
            next = page.Next;
        }
        while (next is not null && !more);

        return new DraftQueueResponse(queued, (int)Spacing.TotalSeconds, more, true);
    }
}
