using System.Diagnostics.CodeAnalysis;

namespace Pipeline.Core.Sources;

public sealed record Source(string Id, string Name, IReadOnlyList<string> Hosts);

/// <summary>
/// Known news sources. Hosts are canonical (no www./m. prefix).
/// TODO: confirm hosts, including historical domains, in the fetch spike.
/// </summary>
public static class SourceCatalog
{
    public static readonly IReadOnlyList<Source> All =
    [
        new("evrensel", "Evrensel", ["evrensel.net"]),
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
