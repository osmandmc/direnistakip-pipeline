// Trims a saved news page into a small parser test fixture, so the repository keeps the page
// structure without republishing the article. Keeps: head metadata, JSON-LD, the first <article>
// with its title, lead and first few body elements (also inside plain wrapper divs), each shortened to its first sentence.
// Removes: other scripts, styles, menus, footer, ads, subscription boxes, the rest of the text.
//
// Usage: dotnet run --project tools/FixtureTrimmer -- <page.html> <fixture.html>

using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

const int KeepBodyElements = 3;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/FixtureTrimmer -- <page.html> <fixture.html>");
    return 1;
}

using var document = new HtmlParser().ParseDocument(File.ReadAllText(args[0]));
var article = document.QuerySelector("article");
if (article is null || document.Body is null || document.Head is null)
{
    Console.Error.WriteLine("No <article> element found; not a page we know how to trim.");
    return 1;
}

// Head: keep metadata and JSON-LD only.
foreach (var element in document.Head.Children.ToList())
{
    var isJsonLd = element.LocalName == "script" && element.GetAttribute("type") == "application/ld+json";
    if (element.LocalName is not ("meta" or "title") && !isJsonLd)
    {
        element.Remove();
    }
}
foreach (var meta in document.Head.QuerySelectorAll("meta[content]"))
{
    var name = meta.GetAttribute("name") ?? meta.GetAttribute("property") ?? "";
    if (name.Contains("description", StringComparison.OrdinalIgnoreCase))
    {
        meta.SetAttribute("content", FirstSentence(meta.GetAttribute("content")!));
    }
}

// Body: only the first article, without ads, share buttons and subscription boxes.
document.Body.InnerHtml = "";
document.Body.AppendChild(article);
foreach (var element in article.QuerySelectorAll(
    "script, style, noscript, svg, iframe, .advertisement-area, [id^='div-gpt-ad'], [class*='evr-sub'], .paylas").ToList())
{
    element.Remove();
}

// Text: lead and the first few body elements, each cut to its first sentence; the rest removed.
if (article.QuerySelector("h2.article-spot") is { } lead)
{
    lead.TextContent = FirstSentence(lead.TextContent);
}
// The text container: news-content, or articleBody on explainer pages; plain wrapper divs inside are trimmed the same way.
if ((article.QuerySelector("div.news-content") ?? article.QuerySelector("[property='articleBody']")) is { } content)
{
    var kept = 0;
    Trim(content);

    void Trim(IElement container)
    {
        foreach (var element in container.Children.ToList())
        {
            if (element.LocalName == "div" && !element.HasAttribute("class") && !element.HasAttribute("id"))
            {
                Trim(element);
            }
            else if (element.LocalName is "p" or "h2" or "h3" or "h4" or "blockquote" or "ul" or "ol" && kept < KeepBodyElements)
            {
                element.TextContent = FirstSentence(element.TextContent);
                kept++;
            }
            else
            {
                element.Remove();
            }
        }
    }
}
foreach (var comment in document.Descendants<IComment>().ToList())
{
    comment.Remove();
}

File.WriteAllText(args[1], "<!doctype html>\n" + document.DocumentElement.OuterHtml + "\n");
Console.WriteLine($"{args[1]}: {new FileInfo(args[0]).Length:N0} → {new FileInfo(args[1]).Length:N0} bytes");
return 0;

static string FirstSentence(string text)
{
    var clean = Regex.Replace(text, @"\s+", " ").Trim();
    // A sentence ends at . ! ? … followed by a space, but not after an initial such as "M.".
    var end = Regex.Match(clean, @"(?<!(^|\s)\p{Lu})[.!?…](\s|$)");
    var sentence = end.Success ? clean[..(end.Index + 1)] : clean;
    return sentence.Length <= 200 ? sentence : sentence[..200].TrimEnd() + "…";
}
