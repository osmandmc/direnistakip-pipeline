using System.Globalization;
using System.Text.Json;
using Pipeline.Contracts;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Extraction;

/// <summary>Values of the Extracted stage's answer in the ledger.</summary>
public static class ExtractionAnswers
{
    /// <summary>A draft with a labour action: shown under "Taslak hazır".</summary>
    public const string Draft = "Draft";

    /// <summary>The model found no labour action; the article stays with the others.</summary>
    public const string NoEvent = "NoEvent";
}

/// <summary>
/// The extraction prompt (docs/extraction/prompt.md, the one researchers can also copy in the app) filled with one
/// article and the list values (docs/extraction/lists.json, a snapshot of the app's lists), and the reading of the
/// model's answer: only what the pipeline needs; the app checks the answer field by field.
/// </summary>
public static class ExtractionPrompt
{
    /// <summary>The prompt file is prompt v2 (docs/extraction/findings.md); change this with every change to the file.</summary>
    public const string Version = "prompt-v2";

    /// <summary>Characters of the body sent: the whole text of nearly every article.</summary>
    public const int TextLength = 15000;

    private static readonly (string Key, string Title)[] Lists =
    [
        ("categories", "Kategoriler"),
        ("reasons", "Direniş nedenleri"),
        ("employmentTypes", "İstihdam biçimleri"),
        ("tradeUnionAuthorities", "Sendika yetkisi"),
        ("protestTypes", "Eylem türleri"),
        ("protestPlaces", "Eylem yerleri"),
        ("interventionTypes", "Müdahaleler"),
        ("cities", "İller"),
    ];

    private static readonly Lazy<string> Template = new(() => Resource("extraction-prompt.md"));

    private static readonly Lazy<string> ListText = new(() =>
    {
        using var json = JsonDocument.Parse(Resource("extraction-lists.json"));
        return string.Join("\n\n", Lists.Select(l =>
            $"### {l.Title}\n" + string.Join('\n', json.RootElement.GetProperty(l.Key).EnumerateArray().Select(v => "- " + v.GetString()))));
    });

    public static string Build(ParsedArticle article)
    {
        var body = article.Body.Trim();
        return Template.Value
            .Replace("{{LISTS}}", ListText.Value)
            .Replace("{{SOURCE}}", SourceCatalog.All.FirstOrDefault(s => s.Id == article.SourceId)?.Name ?? article.SourceId)
            .Replace("{{DATE}}", article.PublishedAt is { } date ? TurkeyTime.Normalize(date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "bilinmiyor")
            .Replace("{{TITLE}}", article.Title ?? "")
            .Replace("{{TEXT}}", body.Length <= TextLength ? body : body[..TextLength] + " […]");
    }

    /// <summary>
    /// The answer's JSON object (also inside a ```json fence), whether it found a labour action, and a one-line summary
    /// for the inbox: "company · first protest type · city / district · start date".
    /// </summary>
    /// <exception cref="FormatException">No JSON object.</exception>
    public static (string Json, bool Relevant, string? Summary) ReadAnswer(string answer)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new FormatException("No JSON object in the answer.");
        }
        var json = answer[start..(end + 1)];
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            var relevant = root.TryGetProperty("relevant", out var r) && r.ValueKind == JsonValueKind.True;
            return (json, relevant, relevant ? Summary(root) : null);
        }
        catch (JsonException e)
        {
            throw new FormatException("The answer is not valid JSON: " + e.Message);
        }
    }

    private static string? Summary(JsonElement root)
    {
        static string? Text(JsonElement parent, params string[] path)
        {
            var node = parent;
            foreach (var step in path)
            {
                if (int.TryParse(step, out var index))
                {
                    if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() <= index) return null;
                    node = node[index];
                }
                else if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(step, out node))
                {
                    return null;
                }
            }
            return node.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(node.GetString()) ? node.GetString()!.Trim() : null;
        }

        var place = string.Join(" / ", new[] { Text(root, "protest", "locations", "0", "city"), Text(root, "protest", "locations", "0", "district") }.Where(s => s != null));
        var parts = new[] { Text(root, "case", "company"), Text(root, "protest", "types", "0"), place, Text(root, "protest", "start_date") }
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Resource(string name)
    {
        using var stream = typeof(ExtractionPrompt).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name + " is not embedded.");
        return new StreamReader(stream).ReadToEnd();
    }
}
