using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Pipeline.Core.Identity;

/// <summary>
/// The one canonicalisation function (D-002, D-024). Article identity depends on its output:
/// changing a rule changes ArticleIds, so every change needs a decision entry and a test row.
/// </summary>
public static partial class UrlCanonicalizer
{
    // Host prefixes that serve the same article as the bare host.
    private static readonly string[] HostPrefixes = ["www.", "m.", "mobil.", "amp."];

    // Sources that move between numbered domains (e.g. kizilbayrak85.net after access blocks).
    private static readonly (Regex Pattern, string Host)[] HostAliases =
    [
        (KizilbayrakHost(), "kizilbayrak.net"),
        (SendikaHost(), "sendika.org"),
    ];

    private static readonly string[] TrackingParamPrefixes = ["utm_"];

    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "dclid", "msclkid", "yclid", "igshid", "mc_cid", "mc_eid",
        "ref", "ref_src", "s", "amp", "output",
    };

    public static bool TryCanonicalize(string? url, [NotNullWhen(true)] out string? canonical)
    {
        canonical = null;
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        foreach (var prefix in HostPrefixes)
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal))
            {
                host = host[prefix.Length..];
                break;
            }
        }
        foreach (var (pattern, alias) in HostAliases)
        {
            if (pattern.IsMatch(host))
            {
                host = alias;
                break;
            }
        }

        var path = uri.AbsolutePath;
        if (path.EndsWith("/amp", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/amp".Length];
        }
        path = path.TrimEnd('/');

        var query = string.Join('&', ParseQuery(uri.Query)
            .Where(p => !IsTracking(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => p.Value.Length == 0 ? p.Key : $"{p.Key}={p.Value}"));

        // Scheme is always https; port and fragment are dropped.
        canonical = $"https://{host}{path}{(query.Length > 0 ? "?" + query : "")}";
        return true;
    }

    public static string Canonicalize(string url) =>
        TryCanonicalize(url, out var canonical)
            ? canonical
            : throw new ArgumentException($"Not an absolute http(s) URL: '{url}'.", nameof(url));

    private static bool IsTracking(string key) =>
        TrackingParams.Contains(key)
        || TrackingParamPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^kizilbayrak\d*\.net$")]
    private static partial Regex KizilbayrakHost();

    [GeneratedRegex(@"^sendika\d*\.org$")]
    private static partial Regex SendikaHost();

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var i = part.IndexOf('=');
                return i < 0
                    ? new KeyValuePair<string, string>(part, "")
                    : new KeyValuePair<string, string>(part[..i], part[(i + 1)..]);
            });
}
