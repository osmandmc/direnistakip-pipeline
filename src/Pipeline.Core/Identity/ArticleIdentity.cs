using System.Security.Cryptography;
using System.Text;

namespace Pipeline.Core.Identity;

/// <summary>ArticleId = lowercase hex SHA-256 of the canonical URL (D-002).</summary>
public static class ArticleIdentity
{
    public static string FromCanonicalUrl(string canonicalUrl) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalUrl)));

    public static string FromUrl(string url) => FromCanonicalUrl(UrlCanonicalizer.Canonicalize(url));
}
