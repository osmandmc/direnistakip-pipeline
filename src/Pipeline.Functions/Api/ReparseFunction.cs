using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Core.Parsing;
using Pipeline.Core.Sources;

namespace Pipeline.Functions.Api;

/// <summary>
/// <c>POST /api/articles/reparse?source=evrensel</c> (articles whose parsing failed) or
/// <c>?source=evrensel&amp;version=evrensel-1</c> (articles read by that parser version): parse their stored pages again
/// with the current parser (B8). Requires a function key (D-025). Answers how many parsed now.
/// </summary>
public sealed class ReparseFunction(ArticleReparsing reparsing)
{
    [Function("ReparseArticles")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "articles/reparse")] HttpRequest req,
        CancellationToken ct)
    {
        var source = req.Query["source"].FirstOrDefault();
        if (source is null || !SourceCatalog.All.Any(s => s.Id == source && s.Enabled))
        {
            return new BadRequestObjectResult(new { error = $"'source' is one of: {string.Join(", ", SourceCatalog.All.Where(s => s.Enabled).Select(s => s.Id))}" });
        }
        var version = req.Query["version"].FirstOrDefault();
        var query = new ReparseQuery(source, FailedOnly: version is null, ParserVersion: version);
        return new OkObjectResult(await reparsing.RunAsync(query, ct));
    }
}
