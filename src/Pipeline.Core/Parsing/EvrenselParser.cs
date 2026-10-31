using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Pipeline.Core.Parsing;

/// <summary>
/// Evrensel article pages (structure checked on 2026-10-18; docs/spikes/t4-fetch-spike.md; second layout 2026-10-21).
/// Pages pre-load other stories further down that reuse the same class names, so every rule
/// is scoped to the first &lt;article&gt; element. Two layouts: news in <c>div.news-content</c>; explainer and service
/// pages ("… ne zaman", "… nasıl yapılır") in <c>[property=articleBody]</c>, whose HTML holds only the introduction.
/// </summary>
public sealed partial class EvrenselParser : IArticleParser
{
    private static readonly HtmlParser Html = new();

    public string SourceId => "evrensel";

    // evrensel-2: the articleBody layout, and text inside plain wrapper divs (2026-10-21). evrensel-3: the picture (og:image).
    public string Version => "evrensel-3";

    public ParseResult Parse(string html)
    {
        using var document = Html.ParseDocument(html);
        var article = document.QuerySelector("article");
        if (article is null)
        {
            return ParseResult.Failed("No <article> element");
        }

        // og:title carries a " - Evrensel" suffix; the page heading does not.
        var title = HtmlParagraphs.Clean(article.QuerySelector("h1.articleTitle")?.TextContent)
            ?? StripSiteSuffix(HtmlParagraphs.Clean(document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")));
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
        var lead = HtmlParagraphs.Clean(article.QuerySelector("h2.article-spot")?.TextContent);
        if (lead is not null)
        {
            paragraphs.Insert(0, lead);
        }

        var publishedAt = ParseDate(document.QuerySelector("meta[name='article:published_time']")?.GetAttribute("content"))
            ?? JsonLdDatePublished(document);

        return ParseResult.Success(title, publishedAt, string.Join("\n\n", paragraphs), HtmlParagraphs.OgImage(document));
    }

    // Paragraphs of the text container, in order (ads and the subscription box are divs with a class, skipped).
    private static List<string> BodyParagraphs(IElement article)
    {
        var paragraphs = new List<string>();
        var content = article.QuerySelector("div.news-content") ?? article.QuerySelector("[property='articleBody']");
        if (content is not null)
        {
            HtmlParagraphs.Add(content, paragraphs);
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

    [GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex HasOffset();

    [GeneratedRegex(@"\s+[-–|]\s+Evrensel\s*$")]
    private static partial Regex SiteSuffix();
}
