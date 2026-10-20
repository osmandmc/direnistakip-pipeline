using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Pipeline.Contracts;

namespace Pipeline.Core.Collecting;

/// <summary>Reads an RSS 2.0 feed into articles to submit: link, title and publication date (no text; Fetch reads the page).</summary>
public static class FeedReader
{
    private static readonly XmlReaderSettings Safe = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    /// <exception cref="FormatException">Not an RSS feed.</exception>
    public static IReadOnlyList<ArticleToSubmit> Read(Stream feed)
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
            var title = item.Element("title")?.Value.Trim();
            articles.Add(new ArticleToSubmit(url, string.IsNullOrEmpty(title) ? null : title, ReadDate(item.Element("pubDate")?.Value)));
        }
        return articles;
    }

    // RFC 822 dates as feeds write them: "Tue, 20 Oct 2026 17:16:00 +0300". Anything else: no date (the page has one).
    private static DateTimeOffset? ReadDate(string? text) =>
        DateTimeOffset.TryParseExact(
            text?.Trim(), ["ddd, dd MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm:ss zzz", "dd MMM yyyy HH:mm:ss zzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
