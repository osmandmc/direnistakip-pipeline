using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Relevance;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Api;

/// <summary>The models the AI stages use, as configured (B13).</summary>
public sealed record AiModels(string Relevance, string Extraction);

/// <summary>
/// <c>GET /api/prompts</c> (B13): the prompts the AI stages run now, with their versions and models, and how many open
/// articles were judged by an older relevance prompt. For the app's "AI istemleri" page. Requires a function key (D-025).
/// </summary>
public sealed class PromptsFunction(AiModels models, ILedgerStore ledger, IListStore lists)
{
    [Function("GetPrompts")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "prompts")] HttpRequest req,
        CancellationToken ct)
    {
        var older = await ledger.CountAsync(new LedgerQuery(
            Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open, RelevanceOlderThan: RelevancePrompt.Version), ct);
        return new OkObjectResult(new PromptsResponse(
        [
            new PromptInfo("relevance", RelevancePrompt.Version, models.Relevance, RelevancePrompt.TemplateText),
            new PromptInfo("extraction", ExtractionPrompt.Version, models.Extraction, ExtractionPrompt.TemplateText),
        ], older, (await lists.GetLatestAsync(ct))?.UpdatedAt));
    }
}
