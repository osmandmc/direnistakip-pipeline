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
/// <c>GET /api/articles?from=2026-10-01&amp;to=2026-10-31&amp;source=evrensel&amp;status=parsed&amp;pageSize=20&amp;next=…</c>
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
        var pageSize = ArticleQueries.DefaultPageSize;
        if (req.Query["pageSize"] is { Count: > 0 } sizeText && !int.TryParse(sizeText, out pageSize))
        {
            return Error($"'pageSize' is a number up to {ArticleQueries.MaxPageSize}");
        }

        var query = new LedgerQuery(
            SourceId: req.Query["source"] is { Count: > 0 } source ? source.ToString() : null,
            From: from is { } f ? TurkeyTime.StartOfDay(f) : null,
            Before: to is { } t ? TurkeyTime.StartOfDay(t.AddDays(1)) : null, // "to" includes the whole day
            Status: status);

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
