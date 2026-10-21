using Pipeline.Contracts;
using Pipeline.Core.Storage;

namespace Pipeline.Core.Parsing;

/// <summary>Which articles to parse again: those whose parsing failed, or those read by one parser version.</summary>
public sealed record ReparseQuery(string SourceId, bool FailedOnly = true, string? ParserVersion = null);

/// <summary>
/// Parses stored pages again with the current parser (B8): after a parser improvement, articles that failed (or were read
/// by an older version) get another chance without a new download. Articles researchers already handled are left alone;
/// articles that came with their text have no stored page and are not touched. At most <see cref="MaxPerRun"/> per call.
/// </summary>
public sealed class ArticleReparsing(ILedgerStore ledger, ArticleParsing parsing)
{
    public const int MaxPerRun = 200;
    private const int PageSize = 100;

    public async Task<ReparseResponse> RunAsync(ReparseQuery query, CancellationToken ct = default)
    {
        var ledgerQuery = new LedgerQuery(
            query.SourceId,
            Status: query.FailedOnly ? ArticleStatusFilter.Failed : ArticleStatusFilter.All,
            Outcome: OutcomeFilter.Open);

        int examined = 0, parsed = 0, stillFailed = 0, noPage = 0;
        string? next = null;
        var more = false;
        do
        {
            var page = await ledger.QueryAsync(ledgerQuery, PageSize, next, ct);
            foreach (var record in page.Items.Where(r => Matches(r, query)))
            {
                if (examined == MaxPerRun)
                {
                    more = true;
                    break;
                }
                examined++;
                switch (await parsing.ReparseAsync(record, ct))
                {
                    case true: parsed++; break;
                    case false: stillFailed++; break;
                    default: noPage++; break;
                }
            }
            next = page.Next;
        }
        while (next is not null && !more);

        return new ReparseResponse(examined, parsed, stillFailed, noPage, more);
    }

    // Failed: the page was downloaded but reading it failed. Version: read (or failed) with that parser version.
    private static bool Matches(LedgerRecord record, ReparseQuery query)
    {
        if (!record.Stages.TryGetValue(Stage.Parsed, out var parse))
        {
            return false;
        }
        return query.ParserVersion is { } version
            ? parse.Version == version
            : parse.Status == StageStatus.Failed && record.Stages.TryGetValue(Stage.Fetched, out var fetch) && fetch.Status == StageStatus.Succeeded;
    }
}
