using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace Pipeline.Core.Parsing;

/// <summary>An article's text as paragraphs, and its picture, shared by the parsers (Evrensel, Sendika.org).</summary>
public static partial class HtmlParagraphs
{
    /// <summary>
    /// Adds the container's paragraphs in order: p, subheadings and quotes as they are, list items with “- ”. Divs with
    /// a class or id hold ads and boxes and are skipped; plain divs only wrap text (e.g. a box pasted from Word) and are read
    /// through, as are divs with one of the classes in <paramref name="textClasses"/> (a site's own text wrappers).
    /// </summary>
    public static void Add(IElement container, List<string> paragraphs, params string[] textClasses)
    {
        foreach (var element in container.Children)
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
                case "div" when (!element.HasAttribute("class") && !element.HasAttribute("id"))
                                || textClasses.Any(c => element.ClassList.Contains(c)):
                    Add(element, paragraphs, textClasses);
                    break;
            }
        }
    }

    /// <summary>The page's og:image when it is an absolute http(s) address, else null.</summary>
    public static string? OgImage(IDocument document) =>
        document.QuerySelector("meta[property='og:image']")?.GetAttribute("content")?.Trim() is { } url
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;

    /// <summary>Whitespace (non-breaking spaces too) collapsed to single spaces; null when nothing is left.</summary>
    public static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }
        var cleaned = Whitespace().Replace(text.Replace(' ', ' '), " ").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
