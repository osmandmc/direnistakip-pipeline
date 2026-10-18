using System.Diagnostics.CodeAnalysis;

namespace Pipeline.Core.Sources;

/// <summary>A news source. Only enabled sources are taken into the pipeline.</summary>
public sealed record Source(string Id, string Name, IReadOnlyList<string> Hosts, bool Enabled = false);

/// <summary>
/// Known news sources. Hosts are canonical (no www./m. prefix; numbered mirror domains
/// are mapped by UrlCanonicalizer). Findings per source: docs/spikes/t4-fetch-spike.md.
/// </summary>
public static class SourceCatalog
{
    public static readonly IReadOnlyList<Source> All =
    [
        // Enabled one at a time as Fetch and Parse support them (phase 2a: Evrensel only).
        new("evrensel", "Evrensel", ["evrensel.net"], Enabled: true),
        new("sendika", "Sendika.org", ["sendika.org"]),
        new("kizilbayrak", "Kızılbayrak", ["kizilbayrak.net"]),
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
