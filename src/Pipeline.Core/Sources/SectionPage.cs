using System.Text.RegularExpressions;

namespace Pipeline.Core.Sources;

/// <summary>
/// For a source without a feed (Sendika.org since its move to Next.js, 2026-10): the web page of the section the
/// pipeline takes, read on the collector's timer instead of a feed. Only its first page is read: older pages are
/// reached through <c>?page=</c>, which Sendika.org's robots.txt disallows. LinkSelector picks the article links of the
/// page's main list (not menus or other boxes); ArticlePath is the pattern an article's path must match.
/// </summary>
public sealed record SectionPage(string Name, string Url, string LinkSelector, string ArticlePath)
{
    public bool IsArticle(Uri link) =>
        link.Host.Equals(new Uri(Url).Host, StringComparison.OrdinalIgnoreCase)
        && Regex.IsMatch(link.AbsolutePath, ArticlePath, RegexOptions.None, TimeSpan.FromSeconds(1));
}
