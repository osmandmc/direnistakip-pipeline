using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Pipeline.Core.Collecting;

/// <summary>
/// Turns the HTML of a feed item's text into plain paragraphs, in the same shape the page parsers produce:
/// one paragraph per block, separated by a blank line; list items as "- item"; images and markup dropped.
/// </summary>
public static partial class FeedText
{
    private static readonly HtmlParser Parser = new();

    public static string? ToParagraphs(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }
        var body = Parser.ParseDocument($"<body>{html}</body>").Body!;

        var paragraphs = new List<string>();
        // Innermost blocks only: a <p> inside a <blockquote> counts once.
        foreach (var block in body.QuerySelectorAll("p, h2, h3, h4, li, blockquote")
                     .Where(e => !e.QuerySelectorAll("p, h2, h3, h4, li").Any()))
        {
            if (Clean(block.TextContent) is not { } text)
            {
                continue;
            }
            text = block.LocalName == "li" ? "- " + text : text;
            // Some items repeat the bold lead as the first body paragraph.
            if (paragraphs.Count == 0 || paragraphs[^1] != text)
            {
                paragraphs.Add(text);
            }
        }
        if (paragraphs.Count == 0 && Clean(body.TextContent) is { } plain)
        {
            paragraphs.Add(plain);
        }
        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static string? Clean(string text)
    {
        var cleaned = Whitespace().Replace(text.Replace(' ', ' '), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
