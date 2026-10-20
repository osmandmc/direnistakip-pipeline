using System.Diagnostics.CodeAnalysis;

namespace Pipeline.Core.Sources;

/// <summary>
/// A news source. Only enabled sources are taken into the pipeline; those with a feed are also collected on a timer.
/// FeedHasFullText: the feed carries each article's whole text, which is taken as is (no page download, no page parser).
/// </summary>
public sealed record Source(
    string Id, string Name, IReadOnlyList<string> Hosts, bool Enabled = false, string? FeedUrl = null, bool FeedHasFullText = false);

/// <summary>
/// Known news sources. Hosts are canonical (no www./m. prefix; numbered mirror domains
/// are mapped by UrlCanonicalizer). Findings per source: docs/spikes/t4-fetch-spike.md.
/// </summary>
public static class SourceCatalog
{
    public static readonly IReadOnlyList<Source> All =
    [
        // Enabled one at a time as Fetch and Parse support them (phase 2a: Evrensel only).
        // Evrensel's news feed holds about the last 100 articles (≈ 14 hours), so a missed run loses nothing.
        new("evrensel", "Evrensel", ["evrensel.net"], Enabled: true, FeedUrl: "https://www.evrensel.net/rss/haber.xml"),
        new("sendika", "Sendika.org", ["sendika.org"]),
        // Kızılbayrak's feed (≈ 50 items, about 3 days) carries the whole text, checked against the pages on 2026-10-20, so
        // its articles need no page parser. The old host over HTTP redirects to the current mirror domain (HTTPS to it
        // fails); article links on mirrors map to kizilbayrak.net (UrlCanonicalizer). Pasted links still go to Fetch and
        // fail at Parse ("No parser"), unless the feed already brought them.
        new("kizilbayrak", "Kızılbayrak", ["kizilbayrak.net"], Enabled: true,
            FeedUrl: "http://www.kizilbayrak.net/rss", FeedHasFullText: true),
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
