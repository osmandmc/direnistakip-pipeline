using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pipeline.Contracts;
using Pipeline.Core.Extraction;
using Pipeline.Core.Storage;

namespace Pipeline.Functions.Api;

/// <summary>
/// The app's list values for the Extract stage (B16). Requires a function key (D-025).
/// <c>PUT /api/lists</c> with <c>{ "lists": { "reasons": [...], "protestTypes": [...], … } }</c>: stores a new version when
/// they changed. <c>GET /api/lists</c>: the latest version's date and counts (404 before the app first sent them).
/// </summary>
public sealed class ListsFunction(ListsUpdate update, IListStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Function("PutLists")]
    public async Task<IActionResult> Put(
        [HttpTrigger(AuthorizationLevel.Function, "put", Route = "lists")] HttpRequest req,
        CancellationToken ct)
    {
        ListsRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ListsRequest>(req.Body, Json, ct);
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "The body is not valid JSON." });
        }
        try
        {
            return new OkObjectResult(await update.PutAsync(request ?? new ListsRequest(null!), ct));
        }
        catch (ArgumentException e)
        {
            return new BadRequestObjectResult(new { error = e.Message, lists = ExtractionPrompt.ListKeys });
        }
    }

    [Function("GetLists")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "lists")] HttpRequest req,
        CancellationToken ct) =>
        await store.GetLatestAsync(ct) is { } lists
            ? new OkObjectResult(new ListsResponse(lists.UpdatedAt, false, lists.Lists.ToDictionary(l => l.Key, l => l.Value.Count)))
            : new NotFoundObjectResult(new { error = "The app has not sent its lists yet; drafts use the snapshot of 2026-10-21." });
}
