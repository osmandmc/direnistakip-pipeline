using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using Pipeline.Contracts;
using Pipeline.Core;
using Pipeline.Core.Articles;
using Pipeline.Core.Storage;

namespace Pipeline.Infrastructure;

/// <summary>Ledger in the Cosmos "ledger" container; id and partition key are the ArticleId (D-020).</summary>
internal sealed class CosmosLedgerStore(Container container) : ILedgerStore
{
    private static readonly ItemRequestOptions NoContentBack = new() { EnableContentResponseOnWrite = false };

    public async Task<bool> TryCreateAsync(LedgerRecord record, CancellationToken ct = default)
    {
        try
        {
            await container.CreateItemAsync(record, new PartitionKey(record.Id), NoContentBack, ct);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    public async Task<LedgerRecord?> GetAsync(string articleId, CancellationToken ct = default)
    {
        try
        {
            var response = await container.ReadItemAsync<LedgerRecord>(articleId, new PartitionKey(articleId), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    // A patch changes only /stages/{stage}; other stages written meanwhile are kept.
    public Task RecordStageAsync(string articleId, Stage stage, StageResult result, CancellationToken ct = default) =>
        container.PatchItemAsync<LedgerRecord>(
            articleId,
            new PartitionKey(articleId),
            [PatchOperation.Set($"/stages/{stage}", result)],
            new PatchItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);

    public async Task SetSummaryAsync(string articleId, string? title, DateTimeOffset? publishedAt, CancellationToken ct = default)
    {
        var operations = new List<PatchOperation>();
        if (title is not null)
        {
            operations.Add(PatchOperation.Set("/title", title));
            // Storage only (not part of LedgerRecord): what title searches compare against.
            operations.Add(PatchOperation.Set("/searchTitle", TitleSearch.Fold(title)));
        }
        if (publishedAt is { } date)
        {
            var turkey = TurkeyTime.Normalize(date);
            operations.Add(PatchOperation.Set("/publishedAt", turkey));
            operations.Add(PatchOperation.Set("/listDate", turkey));
        }
        if (operations.Count > 0)
        {
            await container.PatchItemAsync<LedgerRecord>(
                articleId, new PartitionKey(articleId), operations,
                new PatchItemRequestOptions { EnableContentResponseOnWrite = false }, ct);
        }
    }

    public Task SetOutcomeAsync(string articleId, ArticleOutcome outcome, CancellationToken ct = default) =>
        container.PatchItemAsync<LedgerRecord>(
            articleId,
            new PartitionKey(articleId),
            [PatchOperation.Set("/outcome", outcome with { At = TurkeyTime.Normalize(outcome.At) })],
            new PatchItemRequestOptions { EnableContentResponseOnWrite = false },
            ct);

    public async Task<LedgerPage> QueryAsync(LedgerQuery query, int pageSize, string? continuation, CancellationToken ct = default)
    {
        var definition = Where("SELECT * FROM c", query, " ORDER BY c.listDate DESC");

        using var results = container.GetItemQueryIterator<LedgerRecord>(
            definition,
            ContinuationToken.Decode(continuation),
            new QueryRequestOptions { MaxItemCount = pageSize, ResponseContinuationTokenLimitInKb = 4 });

        // A query across partitions can return an empty page that still has more behind it.
        FeedResponse<LedgerRecord>? page = null;
        try
        {
            while (results.HasMoreResults)
            {
                page = await results.ReadNextAsync(ct);
                if (page.Count > 0)
                {
                    break;
                }
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && continuation is not null)
        {
            throw new ArgumentException("Invalid 'next' value.", nameof(continuation), ex);
        }
        return page is null
            ? new LedgerPage([], null)
            : new LedgerPage(page.ToList(), ContinuationToken.Encode(page.ContinuationToken));
    }

    public async Task<int> CountAsync(LedgerQuery query, CancellationToken ct = default)
    {
        using var results = container.GetItemQueryIterator<int>(Where("SELECT VALUE COUNT(1) FROM c", query, ""));
        var total = 0;
        while (results.HasMoreResults)
        {
            total += (await results.ReadNextAsync(ct)).Sum();
        }
        return total;
    }

    // _ts is when Cosmos last wrote the record (whole seconds): every submission, stage and outcome updates it.
    public async Task<IReadOnlyList<LedgerRecord>> ChangedSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var definition = new QueryDefinition("SELECT * FROM c WHERE c._ts >= @since").WithParameter("@since", since.ToUnixTimeSeconds());
        using var results = container.GetItemQueryIterator<LedgerRecord>(definition);
        var records = new List<LedgerRecord>();
        while (results.HasMoreResults)
        {
            records.AddRange(await results.ReadNextAsync(ct));
        }
        return records;
    }

    /// <summary>The query's filters as SQL, shared by the list and the count.</summary>
    private static QueryDefinition Where(string select, LedgerQuery query, string orderBy)
    {
        // Records created before list dates existed have none and are left out.
        var sql = new StringBuilder(select + " WHERE IS_DEFINED(c.listDate)");
        var parameters = new List<(string Name, object Value)>();
        if (query.SourceId is not null)
        {
            sql.Append(" AND c.sourceId = @source");
            parameters.Add(("@source", query.SourceId));
        }
        // Dates are compared as text, so they must be in the same form as stored: Turkey time, whole seconds.
        if (query.From is { } from)
        {
            sql.Append(" AND c.listDate >= @from");
            parameters.Add(("@from", TurkeyTime.Normalize(from)));
        }
        if (query.Before is { } before)
        {
            sql.Append(" AND c.listDate < @before");
            parameters.Add(("@before", TurkeyTime.Normalize(before)));
        }
        sql.Append(query.Status switch
        {
            ArticleStatusFilter.Parsed => $" AND c.stages.{Stage.Parsed}.status = '{StageStatus.Succeeded}'",
            ArticleStatusFilter.Failed => " AND (" + string.Join(" OR ",
                Enum.GetValues<Stage>().Select(stage => $"c.stages.{stage}.status = '{StageStatus.Failed}'")) + ")",
            _ => "",
        });
        sql.Append(query.Outcome switch
        {
            OutcomeFilter.Open => " AND (NOT IS_DEFINED(c.outcome) OR IS_NULL(c.outcome))",
            OutcomeFilter.Handled => " AND IS_DEFINED(c.outcome) AND NOT IS_NULL(c.outcome)",
            _ => "",
        });
        // An article without a Relevance answer (not assessed, skipped, failed) counts as "other".
        var answer = $"c.stages.{Stage.Relevance}.answer";
        sql.Append(query.Relevance switch
        {
            RelevanceFilter.NotRelevant => $" AND {answer} = '{RelevanceVerdict.NotRelevant}'",
            RelevanceFilter.Other => $" AND (NOT IS_DEFINED({answer}) OR IS_NULL({answer}) OR {answer} != '{RelevanceVerdict.NotRelevant}')",
            _ => "",
        });
        if (query.TitleContains is { } text)
        {
            // Records titled before searchTitle existed: case-insensitive match on the title itself.
            sql.Append(" AND (IS_DEFINED(c.searchTitle) ? CONTAINS(c.searchTitle, @folded) : CONTAINS(c.title, @text, true))");
            parameters.Add(("@folded", TitleSearch.Fold(text)));
            parameters.Add(("@text", text));
        }
        sql.Append(orderBy);

        var definition = new QueryDefinition(sql.ToString());
        foreach (var (name, value) in parameters)
        {
            definition = definition.WithParameter(name, value);
        }
        return definition;
    }
}

/// <summary>Cosmos continuation tokens are JSON; the API passes them as URL-safe Base64 ("next").</summary>
internal static class ContinuationToken
{
    public static string? Encode(string? token) =>
        token is null ? null : Convert.ToBase64String(Encoding.UTF8.GetBytes(token)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string? Decode(string? next)
    {
        if (string.IsNullOrEmpty(next))
        {
            return null;
        }
        var base64 = next.Replace('-', '+').Replace('_', '/');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')));
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid 'next' value.", nameof(next));
        }
    }
}
