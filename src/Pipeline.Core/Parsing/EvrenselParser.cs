using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Pipeline.Core.Parsing;

/// <summary>
/// Evrensel article pages (structure checked on 2026-10-18; docs/spikes/t4-fetch-spike.md).
/// Pages pre-load other stories further down that reuse the same class names, so every rule
/// is scoped to the first &lt;article&gt; element.
/// </summary>
public sealed partial class EvrenselParser : IArticleParser
{
    private static readonly HtmlParser Html = new();

    public string SourceId => "evrensel";

    public string Version => "evrensel-1";

    public ParseResult Parse(string html)
    {
        using var document = Html.ParseDocument(html);
        var article = document.QuerySelector("article");
        if (article is null)
        {
            return ParseResult.Failed("No <article> element");
        }

        // og:title carries a " - Evrensel" suffix; the page heading does not.
        var title = Clean(article.QuerySelector("h1.articleTitle")?.TextContent)
            ?? StripSiteSuffix(Clean(document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")));
        if (title is null)
        {
            return ParseResult.Failed("No title found");
        }

        var paragraphs = BodyParagraphs(article);
        if (paragraphs.Count == 0)
        {
            return ParseResult.Failed("No article body found");
        }

        // The lead (spot) is a one-sentence summary that often names the company and numbers.
        var lead = Clean(article.QuerySelector("h2.article-spot")?.TextContent);
        if (lead is not null)
        {
            paragraphs.Insert(0, lead);
        }

        var publishedAt = ParseDate(document.QuerySelector("meta[name='article:published_time']")?.GetAttribute("content"))
            ?? JsonLdDatePublished(document);

        return ParseResult.Success(title, publishedAt, string.Join("\n\n", paragraphs));
    }

    // Only direct children of the text container: the subscription box and ads sit in nested divs.
    private static List<string> BodyParagraphs(IElement article)
    {
        var paragraphs = new List<string>();
        var content = article.QuerySelector("div.news-content");
        if (content is null)
        {
            return paragraphs;
        }

        foreach (var element in content.Children)
        {
            switch (element.LocalName)
            {
                case "p" or "h2" or "h3" or "h4" or "blockquote":
                    if (Clean(element.TextContent) is { } text)
                    {
                        paragraphs.Add(text);
                    }
                    break;
                case "ul" or "ol":
                    foreach (var item in element.Children.Where(c => c.LocalName == "li"))
                    {
                        if (Clean(item.TextContent) is { } itemText)
                        {
                            paragraphs.Add("- " + itemText);
                        }
                    }
                    break;
            }
        }
        return paragraphs;
    }

    private static DateTimeOffset? JsonLdDatePublished(IDocument document)
    {
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                if (json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("datePublished", out var date)
                    && ParseDate(date.GetString()) is { } parsed)
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Broken JSON-LD on a page is not our problem; try the next block.
            }
        }
        return null;
    }

    /// <summary>ISO dates; without a time zone they are taken as Turkey time.</summary>
    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (HasOffset().IsMatch(value))
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
                ? withOffset
                : null;
        }
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TurkeyTime.Offset)
            : null;
    }

    private static string? StripSiteSuffix(string? title) =>
        title is null ? null : SiteSuffix().Replace(title, "") is { Length: > 0 } stripped ? stripped : null;

    private static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }
        var cleaned = Whitespace().Replace(text.Replace(' ', ' '), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex HasOffset();

    [GeneratedRegex(@"\s+[-–|]\s+Evrensel\s*$")]
    private static partial Regex SiteSuffix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
