// Runs the relevance prompt (docs/extraction/relevance-prompt.md) over labelled articles and writes one line per answer.
//
//   dotnet user-secrets set Gemini:ApiKey <key> --project tools/RelevanceEval     (once; or the Gemini__ApiKey variable)
//   dotnet run --project tools/RelevanceEval -- <articles.jsonl> <answers.jsonl> [model]
//
// Input lines: { "id", "label", "source", "date", "title", "text" } (label: what the researchers decided). Output lines:
// { "id", "label", "verdict", "reason", "model" } or { "id", "error" }. Ids already answered are skipped, so a run can be
// repeated: it continues an interrupted run and tries the failed ones again (the latest line per id counts). Requests are spaced to stay inside the free tier's per-minute limit. Article texts stay
// local: keep both files out of the repository.
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Pipeline.Core.Relevance;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RelevanceEval <articles.jsonl> <answers.jsonl> [model]");
    return 2;
}

var config = new ConfigurationBuilder().AddUserSecrets(typeof(Program).Assembly).AddEnvironmentVariables().Build();
var options = new GeminiOptions { ApiKey = config["Gemini:ApiKey"] };
if (config["Gemini:Model"] is { Length: > 0 } configured)
{
    options.Model = configured;
}
if (args.Length > 2)
{
    options.Model = args[2];
}
if (!options.HasKey)
{
    Console.Error.WriteLine("No Gemini:ApiKey: set it with 'dotnet user-secrets set Gemini:ApiKey <key> --project tools/RelevanceEval'.");
    return 2;
}

var model = new GeminiModel(new HttpClient { Timeout = TimeSpan.FromSeconds(120) }, options);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var done = File.Exists(args[1])
    ? File.ReadLines(args[1]).Select(l => JsonDocument.Parse(l).RootElement)
        .Where(r => !r.TryGetProperty("error", out _)).Select(r => r.GetProperty("id").GetString()).ToHashSet()
    : [];
var spacing = TimeSpan.FromSeconds(7); // ≈ 8 a minute, under the free tier's limit

using var output = new StreamWriter(args[1], append: true);
var count = 0;
foreach (var line in File.ReadLines(args[0]))
{
    var article = JsonDocument.Parse(line).RootElement;
    var id = article.GetProperty("id").GetString()!;
    if (done.Contains(id))
    {
        continue;
    }
    DateTimeOffset? date = DateTimeOffset.TryParse(article.GetProperty("date").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    var prompt = RelevancePrompt.Build(
        Name(article.GetProperty("source").GetString()!), date, article.GetProperty("title").GetString(), article.GetProperty("text").GetString() ?? "");

    object result;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var answer = await model.AnswerJsonAsync(prompt);
            var (verdict, reason) = RelevancePrompt.ReadAnswer(answer.Text);
            result = new { id, label = article.GetProperty("label").GetString(), verdict, reason, model = answer.Model };
            break;
        }
        catch (ModelRateLimitedException e) when (attempt < 5)
        {
            var wait = e.RetryAfter > TimeSpan.FromMinutes(1) ? TimeSpan.FromSeconds(30) : e.RetryAfter;
            Console.WriteLine($"  {e.Message[..Math.Min(60, e.Message.Length)]}, waiting {wait.TotalSeconds:0} s");
            await Task.Delay(wait);
        }
        catch (TaskCanceledException) when (attempt < 3)
        {
            Console.WriteLine("  no answer in time, waiting 30 s");
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        catch (Exception e) when (e is FormatException or HttpRequestException or TaskCanceledException or ModelRateLimitedException)
        {
            result = new { id, error = e.Message };
            break;
        }
    }
    await output.WriteLineAsync(JsonSerializer.Serialize(result, json));
    await output.FlushAsync();
    Console.WriteLine($"{++count,3} {id}: {JsonSerializer.Serialize(result, json)}");
    await Task.Delay(spacing);
}
Console.WriteLine($"Done: {count} new answers in {args[1]}.");
return 0;

static string Name(string sourceId) =>
    Pipeline.Core.Sources.SourceCatalog.All.FirstOrDefault(s => s.Id == sourceId)?.Name ?? sourceId;
