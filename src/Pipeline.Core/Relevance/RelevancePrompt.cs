using System.Globalization;
using System.Text.Json;
using Pipeline.Contracts;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Relevance;

/// <summary>
/// The relevance prompt (docs/extraction/relevance-prompt.md, embedded) filled with one article, and the reading of
/// the model's answer. Only the start of the text is sent: enough to judge, and a fraction of the tokens.
/// </summary>
public static class RelevancePrompt
{
    /// <summary>Change it with every change to the prompt file, so records say which prompt judged them.</summary>
    public const string Version = "relevance-v3.1";

    /// <summary>Characters of the body sent to the model.</summary>
    public const int TextLength = 1500;

    private static readonly Lazy<string> Template = new(() =>
    {
        using var stream = typeof(RelevancePrompt).Assembly.GetManifestResourceStream("relevance-prompt.md")
            ?? throw new InvalidOperationException("relevance-prompt.md is not embedded.");
        return new StreamReader(stream).ReadToEnd();
    });

    /// <summary>The prompt with its placeholders, as the prompts endpoint shows it (B13).</summary>
    public static string TemplateText => Template.Value;

    /// <summary>Judged by another version of this prompt (its stage version is "prompt · model").</summary>
    public static bool IsOlder(LedgerRecord record) =>
        record.Stages.TryGetValue(Stage.Relevance, out var stage) && stage.Status == StageStatus.Succeeded
            && !(stage.Version ?? "").StartsWith(Version + " ", StringComparison.Ordinal);

    public static string Build(ParsedArticle article) => Build(
        SourceCatalog.All.FirstOrDefault(s => s.Id == article.SourceId)?.Name ?? article.SourceId,
        article.PublishedAt, article.Title, article.Body);

    public static string Build(string source, DateTimeOffset? publishedAt, string? title, string body) => Template.Value
        .Replace("{{SOURCE}}", source)
        .Replace("{{DATE}}", publishedAt is { } date ? TurkeyTime.Normalize(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "bilinmiyor")
        .Replace("{{TITLE}}", title ?? "")
        .Replace("{{TEXT}}", Start(body ?? ""));

    /// <summary>The first <see cref="TextLength"/> characters, cut at a space, marked when cut.</summary>
    public static string Start(string body)
    {
        var text = body.Trim();
        if (text.Length <= TextLength)
        {
            return text;
        }
        var cut = text.LastIndexOf(' ', TextLength);
        return text[..(cut > TextLength / 2 ? cut : TextLength)] + " […]";
    }

    /// <summary>The verdict and reason in the model's answer; also when wrapped in a ```json fence.</summary>
    /// <exception cref="FormatException">No usable verdict.</exception>
    public static (RelevanceVerdict Verdict, string? Reason) ReadAnswer(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new FormatException("No JSON object in the answer.");
        }
        try
        {
            using var json = JsonDocument.Parse(answer[start..(end + 1)]);
            var root = json.RootElement;
            var verdict = root.TryGetProperty("verdict", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()?.Trim() : null;
            return (verdict?.Trim().ToLowerInvariant() switch
            {
                "relevant" => RelevanceVerdict.Relevant,
                "not_relevant" or "notrelevant" or "not relevant" or "not-relevant" or "irrelevant" => RelevanceVerdict.NotRelevant,
                "uncertain" => RelevanceVerdict.Uncertain,
                _ => throw new FormatException($"Unknown verdict '{verdict}'."),
            }, string.IsNullOrEmpty(reason) ? null : reason);
        }
        catch (JsonException e)
        {
            throw new FormatException("The answer is not valid JSON: " + e.Message);
        }
    }
}
