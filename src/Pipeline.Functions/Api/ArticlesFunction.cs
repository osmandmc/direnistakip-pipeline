using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Core;
using Pipeline.Core.Articles;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Api;

/// <summary>
/// Read API for the direnistakip app (D-009). Requires a function key (D-025).
/// <c>GET /api/articles?from=2026-10-01&amp;to=2026-10-31&amp;source=evrensel&amp;status=parsed&amp;outcome=open&amp;relevance=other&amp;q=işçi&amp;pageSize=20&amp;next=…</c>
/// <c>relevance</c> (B10): <c>notRelevant</c> = judged not relevant by the Relevance stage; <c>other</c> = everything else
/// (relevant, uncertain, not assessed); <c>any</c> (default). <c>draft</c> (B12): <c>ready</c> = an AI draft with a labour
/// action; <c>none</c> = no such draft; <c>any</c> (default).
/// <c>q</c> searches titles (Turkish letters and case do not matter); a link in <c>q</c> finds that one article, ignoring the other filters.
/// <c>GET /api/articles/{id}</c>
/// </summary>
public sealed class ArticlesFunction(ArticleQueries queries)
{
    [Function("ListArticles")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "articles")] HttpRequest req,
        CancellationToken ct)
    {
        if (!TryReadDay(req, "from", out var from) || !TryReadDay(req, "to", out var to))
        {
            return Error("'from' and 'to' are dates like 2026-10-31");
        }
        var status = ArticleStatusFilter.Parsed;
        if (req.Query["status"] is { Count: > 0 } statusText && !Enum.TryParse(statusText.ToString(), ignoreCase: true, out status))
        {
            return Error("'status' is one of: parsed, failed, all");
        }
        var outcome = OutcomeFilter.Open;
        if (req.Query["outcome"] is { Count: > 0 } outcomeText && !Enum.TryParse(outcomeText.ToString(), ignoreCase: true, out outcome))
        {
            return Error("'outcome' is one of: open, handled, any");
        }
        var relevance = RelevanceFilter.Any;
        if (req.Query["relevance"] is { Count: > 0 } relevanceText && !Enum.TryParse(relevanceText.ToString(), ignoreCase: true, out relevance))
        {
            return Error("'relevance' is one of: any, notRelevant, other");
        }
        var draft = DraftFilter.Any;
        if (req.Query["draft"] is { Count: > 0 } draftText && !Enum.TryParse(draftText.ToString(), ignoreCase: true, out draft))
        {
            return Error("'draft' is one of: any, ready, none");
        }
        var search = req.Query["q"].ToString().Trim();
        if (search.Length > 0 && await queries.FindByUrlAsync(search, ct) is { } byUrl)
        {
            return new OkObjectResult(byUrl);
        }
        if (search.Length > 0 && TitleSearch.Fold(search).Length < TitleSearch.MinLength)
        {
            return Error($"'q' needs at least {TitleSearch.MinLength} characters");
        }
        var pageSize = ArticleQueries.DefaultPageSize;
        if (req.Query["pageSize"] is { Count: > 0 } sizeText && !int.TryParse(sizeText, out pageSize))
        {
            return Error($"'pageSize' is a number up to {ArticleQueries.MaxPageSize}");
        }

        var query = new LedgerQuery(
            SourceId: req.Query["source"] is { Count: > 0 } source ? source.ToString() : null,
            From: from is { } f ? TurkeyTime.StartOfDay(f) : null,
            Before: to is { } t ? TurkeyTime.StartOfDay(t.AddDays(1)) : null, // "to" includes the whole day
            Status: status,
            Outcome: outcome,
            TitleContains: search.Length > 0 ? search : null,
            Relevance: relevance,
            Draft: draft);

        try
        {
            return new OkObjectResult(await queries.ListAsync(query, pageSize, req.Query["next"].FirstOrDefault(), ct));
        }
        catch (ArgumentException ex)
        {
            return Error(ex.Message);
        }
    }

    [Function("GetArticle")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "articles/{id}")] HttpRequest req,
        string id,
        CancellationToken ct) =>
        await queries.GetAsync(id, ct) is { } article
            ? new OkObjectResult(article)
            : new NotFoundObjectResult(new { error = $"No article with id '{id}'" });

    private static bool TryReadDay(HttpRequest req, string name, out DateOnly? day)
    {
        day = null;
        if (req.Query[name] is not { Count: > 0 } text)
        {
            return true;
        }
        if (DateOnly.TryParseExact(text.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            day = parsed;
            return true;
        }
        return false;
    }

    private static BadRequestObjectResult Error(string message) => new(new { error = message });
}
