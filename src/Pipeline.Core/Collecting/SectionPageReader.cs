using AngleSharp.Html.Parser;
using Pipeline.Contracts;
using Pipeline.Core.Identity;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Collecting;

/// <summary>
/// Reads a section's web page (S1, sources without a feed) into articles to submit: the links of its main list, each
/// once, with the link's text as title when it has one. The page carries no dates; Parse takes them from the article.
/// </summary>
public static class SectionPageReader
{
    private static readonly HtmlParser Html = new();

    /// <exception cref="FormatException">No article link on the page (the site's layout changed).</exception>
    public static IReadOnlyList<ArticleToSubmit> Read(string html, SectionPage section)
    {
        using var document = Html.ParseDocument(html);
        var page = new Uri(section.Url);
        var articles = new List<ArticleToSubmit>();
        var seen = new Dictionary<string, int>();
        foreach (var link in document.QuerySelectorAll(section.LinkSelector))
        {
            if (!Uri.TryCreate(page, link.GetAttribute("href"), out var url) || !section.IsArticle(url)
                || !UrlCanonicalizer.TryCanonicalize(url.AbsoluteUri, out var canonical))
            {
                continue;
            }
            var title = Clean(link.TextContent);
            // The same article is linked from its picture (no text) and from its title.
            if (seen.TryGetValue(canonical, out var index))
            {
                if (articles[index].Title is null && title is not null)
                {
                    articles[index] = articles[index] with { Title = title };
                }
                continue;
            }
            seen[canonical] = articles.Count;
            articles.Add(new ArticleToSubmit(url.AbsoluteUri, title, null, null));
        }
        if (articles.Count == 0)
        {
            throw new FormatException($"No article links on the {section.Name} page ({section.LinkSelector}).");
        }
        return articles;
    }

    private static string? Clean(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) is { Length: > 0 } cleaned ? cleaned : null;
}
