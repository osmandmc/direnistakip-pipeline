using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Pipeline.Contracts;
using Pipeline.Core.Sources;

namespace Pipeline.Core.Collecting;

/// <summary>
/// Reads an RSS 2.0 feed into articles to submit: link, title and publication date. With <c>withText</c> (feeds that
/// carry the whole article, like Kızılbayrak's) also the text, so the article enters already parsed; otherwise Fetch reads the page.
/// With a <see cref="FeedSection"/>, items outside it are left out (B18).
/// </summary>
public static class FeedReader
{
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <exception cref="FormatException">Not an RSS feed.</exception>
    public static IReadOnlyList<ArticleToSubmit> Read(Stream feed, bool withText = false, FeedSection? section = null) =>
        ReadAll(feed, withText, section).Taken;

    /// <summary>The items in the section, and how many were left out.</summary>
    public static (IReadOnlyList<ArticleToSubmit> Taken, int OutsideSection) ReadAll(Stream feed, bool withText = false, FeedSection? section = null)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(feed, Safe);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"The feed is not valid XML: {ex.Message}", ex);
        }
        var channel = document.Root?.Name.LocalName == "rss" ? document.Root.Element("channel") : null;
        if (channel is null)
        {
            throw new FormatException("Not an RSS 2.0 feed (no rss/channel).");
        }

        var articles = new List<ArticleToSubmit>();
        var outside = 0;
        foreach (var item in channel.Elements("item"))
        {
            // The guid is the plain article link; "link" carries tracking parameters. Either gives the same ArticleId.
            var url = new[] { item.Element("guid")?.Value, item.Element("link")?.Value }
                .Select(u => u?.Trim())
                .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
            if (url is null)
            {
                continue;
            }
            // The link is checked as the feed gives it (Kızılbayrak's guid is only a slug).
            var link = item.Element("link")?.Value.Trim() ?? url;
            if (section is not null && !section.Matches(item.Elements("category").Select(c => c.Value), link))
            {
                outside++;
                continue;
            }
            var title = item.Element("title")?.Value.Trim();
            var text = withText ? FeedText.ToParagraphs(item.Element("description")?.Value) : null;
            articles.Add(new ArticleToSubmit(url, string.IsNullOrEmpty(title) ? null : title, ReadDate(item.Element("pubDate")?.Value), text));
        }
        return (articles, outside);
    }

    // RFC 822 dates as feeds write them: "Tue, 20 Oct 2026 17:16:00 +0300". Anything else: no date (the page has one).
    private static DateTimeOffset? ReadDate(string? text) =>
        DateTimeOffset.TryParseExact(
            text?.Trim(), ["ddd, dd MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm:ss zzz", "dd MMM yyyy HH:mm:ss zzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
