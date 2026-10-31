using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Pipeline.Core.Parsing;

/// <summary>
/// Sendika.org article pages (Next.js, structure checked on 2026-10-31). The server's HTML holds the article in
/// <c>.postContent</c>: the title in <c>h3.title</c>, the lead in <c>div.spot</c>, the text in <c>#news</c> (after its
/// first paragraph, inside <c>div.detail-of-news</c>). A first <c>.postContent</c> is only a loading placeholder; the one
/// with the title comes later in the page (streamed). The date is drawn in the browser, so it is only in the page's
/// embedded data (the <c>self.__next_f</c> scripts), as the <c>"time"</c> of the date component in the article's
/// <c>metas</c> block, in UTC. Other stories listed on the page have dates too, so only the one right after <c>metas</c>
/// is taken. The picture is the page's og:image.
/// </summary>
public sealed partial class SendikaParser : IArticleParser
{
    private static readonly HtmlParser Html = new();

    public string SourceId => "sendika";

    public string Version => "sendika-1";

    public ParseResult Parse(string html)
    {
        using var document = Html.ParseDocument(html);
        var post = document.QuerySelectorAll(".postContent").FirstOrDefault(p => p.QuerySelector("h3.title") is not null);
        var title = HtmlParagraphs.Clean(post?.QuerySelector("h3.title")?.TextContent)
            ?? HtmlParagraphs.Clean(document.QuerySelector("meta[property='og:title']")?.GetAttribute("content"));
        if (title is null)
        {
            return ParseResult.Failed("No title found");
        }

        var paragraphs = new List<string>();
        if ((post?.QuerySelector("#news") ?? document.GetElementById("news")) is { } news)
        {
            HtmlParagraphs.Add(news, paragraphs, "detail-of-news");
        }
        if (paragraphs.Count == 0)
        {
            return ParseResult.Failed("No article body found");
        }

        // The lead (spot) summarises the story, often with the workplace and the numbers.
        var lead = new List<string>();
        if (post?.QuerySelector("div.spot") is { } spot)
        {
            HtmlParagraphs.Add(spot, lead);
            if (lead.Count == 0 && HtmlParagraphs.Clean(spot.TextContent) is { } text)
            {
                lead.Add(text);
            }
        }
        paragraphs.InsertRange(0, lead);

        return ParseResult.Success(title, PublishedAt(html), string.Join("\n\n", paragraphs), HtmlParagraphs.OgImage(document));
    }

    private static DateTimeOffset? PublishedAt(string html) =>
        MetasTime().Match(html) is { Success: true } match
        && DateTimeOffset.TryParse(match.Groups["time"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    // In the embedded data quotes are escaped (\"time\":\"…\"); the date component follows "metas" within a few dozen characters.
    [GeneratedRegex(@"metas\\?""[^{]{0,200}\{\\?""time\\?""\s*:\s*\\?""(?<time>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z)")]
    private static partial Regex MetasTime();
}
