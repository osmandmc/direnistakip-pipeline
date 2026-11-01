using System.Globalization;
using AngleSharp.Html.Parser;

namespace Pipeline.Core.Parsing;

/// <summary>
/// Kızılbayrak article pages (ASP.NET Web Forms, structure checked on 2026-11-01). Articles from the feed carry their
/// text and need no parser; this one is for links pasted by researchers, e.g. older articles the feed (about three
/// days) no longer holds. Fixed element ids: the title in <c>lblHaberBaslik</c>, the lead in <c>lblHaberSpotu</c>, the
/// text in <c>lblHaberIcerik</c>, the date and time in <c>lblHaberTarih</c> ("30 Ocak 2026") and <c>lblHaberSaat</c>
/// ("13:35"), Turkey time. The picture is the page's og:image.
/// </summary>
public sealed class KizilbayrakParser : IArticleParser
{
    private static readonly HtmlParser Html = new();
    private static readonly CultureInfo Turkish = new("tr-TR");
    private const string Prefix = "ContentPlaceHolder1_";

    public string SourceId => "kizilbayrak";

    public string Version => "kizilbayrak-1";

    public ParseResult Parse(string html)
    {
        using var document = Html.ParseDocument(html);
        var title = HtmlParagraphs.Clean(document.GetElementById(Prefix + "lblHaberBaslik")?.TextContent)
            ?? HtmlParagraphs.Clean(document.QuerySelector("meta[property='og:title']")?.GetAttribute("content"));
        if (title is null)
        {
            return ParseResult.Failed("No title found");
        }

        var paragraphs = new List<string>();
        if (document.GetElementById(Prefix + "lblHaberIcerik") is { } text)
        {
            HtmlParagraphs.Add(text, paragraphs);
        }
        if (paragraphs.Count == 0)
        {
            return ParseResult.Failed("No article body found");
        }

        // The lead (spot) summarises the story; it comes first, as with the other sources.
        if (HtmlParagraphs.Clean(document.GetElementById(Prefix + "lblHaberSpotu")?.TextContent) is { } lead)
        {
            paragraphs.Insert(0, lead);
        }

        var publishedAt = PublishedAt(
            HtmlParagraphs.Clean(document.GetElementById(Prefix + "lblHaberTarih")?.TextContent),
            HtmlParagraphs.Clean(document.GetElementById(Prefix + "lblHaberSaat")?.TextContent));
        return ParseResult.Success(title, publishedAt, string.Join("\n\n", paragraphs), HtmlParagraphs.OgImage(document));
    }

    /// <summary>"30 Ocak 2026" and "13:35" in Turkey time; the time may be missing (then midnight).</summary>
    private static DateTimeOffset? PublishedAt(string? date, string? time)
    {
        if (date is null
            || !DateTime.TryParseExact(date, ["d MMMM yyyy", "dd MMMM yyyy"], Turkish, DateTimeStyles.None, out var day))
        {
            return null;
        }
        if (time is not null && TimeOnly.TryParseExact(time, ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            day = day.Add(clock.ToTimeSpan());
        }
        return new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TurkeyTime.Offset);
    }
}
