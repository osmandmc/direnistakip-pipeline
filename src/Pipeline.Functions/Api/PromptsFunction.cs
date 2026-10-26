using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Prompts;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Api;

/// <summary>The models the AI stages use, as configured (B13).</summary>
public sealed record AiModels(string Relevance, string Extraction);

/// <summary>
/// The AI stages' prompts, for the app's "AI istemleri" page. Requires a function key (D-025).
/// <c>GET /api/prompts</c> (B13): the prompts the stages run now, with their versions, models and the versions saved in
/// the app, and how many open articles were judged by another relevance prompt.
/// B17 (app R28): <c>POST /api/prompts/{name}/versions</c> saves a new version; <c>POST …/versions/{version}/try</c> runs
/// it on up to 10 articles without saving anything; <c>POST /api/prompts/{name}/active</c> activates a version, or
/// "builtin" for the repository's prompt.
/// </summary>
public sealed class PromptsFunction(AiModels models, ILedgerStore ledger, IListStore lists, IPromptStore store, ActivePrompts active, PromptEditing editing)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("GetPrompts")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "prompts")] HttpRequest req,
        CancellationToken ct)
    {
        var relevance = await active.GetAsync(ActivePrompts.Relevance, ct);
        var extraction = await active.GetAsync(ActivePrompts.Extraction, ct);
        var older = await ledger.CountAsync(new LedgerQuery(
            Status: ArticleStatusFilter.Parsed, Outcome: OutcomeFilter.Open, RelevanceOlderThan: relevance.Version), ct);
        return new OkObjectResult(new PromptsResponse(
        [
            await InfoAsync(ActivePrompts.Relevance, relevance, models.Relevance, ct),
            await InfoAsync(ActivePrompts.Extraction, extraction, models.Extraction, ct),
        ], older, (await lists.GetLatestAsync(ct))?.UpdatedAt));
    }

    private async Task<PromptInfo> InfoAsync(string name, ActivePrompt prompt, string model, CancellationToken ct)
    {
        var versions = (await store.ListAsync(name, ct))
            .Select(v => new PromptVersionInfo(v.Version, v.Text, v.Note, v.CreatedBy, v.CreatedAt, v.ActivatedAt, v.Version == prompt.Version))
            .ToList();
        return new PromptInfo(name, prompt.Version, model, prompt.Template, ActivePrompts.Builtin(name).Version, versions);
    }

    [Function("CreatePromptVersion")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "prompts/{name}/versions")] HttpRequest req,
        string name,
        CancellationToken ct)
    {
        if (await ReadAsync<PromptVersionRequest>(req, ct) is not { } request)
            return new BadRequestObjectResult(new { error = "The body is not valid JSON." });
        try
        {
            var version = await editing.CreateAsync(name, request, ct);
            return new OkObjectResult(new PromptVersionInfo(version.Version, version.Text, version.Note, version.CreatedBy, version.CreatedAt, null, false));
        }
        catch (ArgumentException e)
        {
            return new BadRequestObjectResult(new { error = e.Message });
        }
    }

    [Function("TryPromptVersion")]
    public async Task<IActionResult> Try(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "prompts/{name}/versions/{version}/try")] HttpRequest req,
        string name,
        string version,
        CancellationToken ct)
    {
        if (await ReadAsync<PromptTryRequest>(req, ct) is not { } request)
            return new BadRequestObjectResult(new { error = "The body is not valid JSON." });
        try
        {
            return await editing.TryAsync(name, version, request.ArticleIds ?? [], ct) is { } result
                ? new OkObjectResult(result)
                : new NotFoundObjectResult(new { error = $"No version {version} of {name}." });
        }
        catch (ArgumentException e)
        {
            return new BadRequestObjectResult(new { error = e.Message });
        }
    }

    [Function("ActivatePromptVersion")]
    public async Task<IActionResult> Activate(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "prompts/{name}/active")] HttpRequest req,
        string name,
        CancellationToken ct)
    {
        if (await ReadAsync<PromptActivateRequest>(req, ct) is not { Version.Length: > 0 } request)
            return new BadRequestObjectResult(new { error = "Send { version } (or \"builtin\")." });
        try
        {
            return await editing.ActivateAsync(name, request.Version, request.By, ct) is { } prompt
                ? new OkObjectResult(new { name, version = prompt.Version })
                : new NotFoundObjectResult(new { error = $"No version {request.Version} of {name}." });
        }
        catch (ArgumentException e)
        {
            return new BadRequestObjectResult(new { error = e.Message });
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpRequest req, CancellationToken ct) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, Json, ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
