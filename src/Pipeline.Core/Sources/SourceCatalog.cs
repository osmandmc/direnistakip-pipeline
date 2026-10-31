using System.Diagnostics.CodeAnalysis;

namespace Pipeline.Core.Sources;

/// <summary>
/// A news source. Only enabled sources are taken into the pipeline; those with a feed are also collected on a timer.
/// FeedHasFullText: the feed carries each article's whole text, which is taken as is (no page download, no page parser).
/// FeedSection: only this part of the feed is taken (B18); null: all of it.
/// SectionPage: a source without a feed is collected from its section's web page instead (S1).
/// </summary>
public sealed record Source(
    string Id, string Name, IReadOnlyList<string> Hosts, bool Enabled = false, string? FeedUrl = null, bool FeedHasFullText = false,
    FeedSection? FeedSection = null, SectionPage? SectionPage = null)
{
    public bool IsCollected => Enabled && (FeedUrl is not null || SectionPage is not null);
}

/// <summary>
/// Known news sources. Hosts are canonical (no www./m. prefix; numbered mirror domains
/// are mapped by UrlCanonicalizer). Findings per source: docs/spikes/t4-fetch-spike.md.
/// </summary>
public static class SourceCatalog
{
    public static readonly IReadOnlyList<Source> All =
    [
        // Enabled one at a time as Fetch and Parse support them.
        // Evrensel's news feed holds about the last 100 articles (≈ 14 hours), so a missed run loses nothing. Only its
        // İşçi-Sendika section is taken (B18): about 10 of its ≈ 150 articles a day. (/rss/isci-sendika.xml exists but is empty.)
        new("evrensel", "Evrensel", ["evrensel.net"], Enabled: true, FeedUrl: "https://www.evrensel.net/rss/haber.xml",
            FeedSection: new FeedSection("İşçi-Sendika", Categories: ["İşçi-Sendika"])),
        // Sendika.org has no feed since its move to Next.js (/feed, /rss and the sitemap answer 404, checked 2026-10-31):
        // its Emek-Sermaye section page is read instead (S1). The page lists the newest 12 articles, about a day's worth,
        // each linked twice (picture and title) from div.widgetContent; article paths are /yyyy/mm/<slug>-<number>.
        new("sendika", "Sendika.org", ["sendika.org"], Enabled: true,
            SectionPage: new SectionPage("Emek-Sermaye", "https://sendika.org/category/emek-sermaye", ".widgetContent a[href]", @"^/\d{4}/\d{2}/[^/]+-\d+/?$")),
        // Kızılbayrak's feed (≈ 50 items, about 3 days) carries the whole text, checked against the pages on 2026-10-20, so
        // its articles need no page parser. The old host over HTTP redirects to the current mirror domain (HTTPS to it
        // fails); article links on mirrors map to kizilbayrak.net (UrlCanonicalizer). Pasted links still go to Fetch and
        // fail at Parse ("No parser"), unless the feed already brought them.
        // Only its Sınıf section is taken (B18): the feed has no categories, the links say the section (/haber/sinif/…).
        new("kizilbayrak", "Kızılbayrak", ["kizilbayrak.net"], Enabled: true,
            FeedUrl: "http://www.kizilbayrak.net/rss", FeedHasFullText: true, FeedSection: new FeedSection("Sınıf", PathSegment: "sinif")),
        new("birgun", "BirGün", ["birgun.net"]),
    ];

    public static bool TryResolve(string canonicalUrl, [NotNullWhen(true)] out Source? source)
    {
        source = null;
        if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }
        source = All.FirstOrDefault(s => s.Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));
        return source is not null;
    }
}
