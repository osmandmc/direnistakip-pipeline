using System.Globalization;
using System.Text;

namespace Pipeline.Core.Sources;

/// <summary>
/// The part of a source's feed the pipeline takes (B18): only these articles are submitted, so pages outside it are
/// never downloaded nor sent to the AI. Categories: the item's &lt;category&gt; values (Evrensel writes several separated by
/// commas, with stray combining dots: "İşçi̇-Sendi̇ka"). PathSegment: a segment of the article's link (Kızılbayrak has no
/// categories, its links say the section: /ana-sayfa/haber/sinif/…). Compared without case, accents or Turkish letters.
/// Links pasted by researchers and uploads are not filtered: they were chosen on purpose.
/// </summary>
public sealed record FeedSection(string Name, IReadOnlyList<string>? Categories = null, string? PathSegment = null)
{
    public bool Matches(IEnumerable<string> categories, string url)
    {
        if (Categories is { Count: > 0 } wanted)
        {
            var keys = wanted.Select(Key).ToHashSet();
            if (categories.SelectMany(c => c.Split(',')).Any(c => keys.Contains(Key(c))))
                return true;
        }
        if (PathSegment is { } segment && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var key = Key(segment);
            if (uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(s => Key(s) == key))
                return true;
        }
        return false;
    }

    /// <summary>"İşçi̇-Sendi̇ka" → "isci-sendika".</summary>
    public static string Key(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                folded.Append(c == 'ı' ? 'i' : char.ToLowerInvariant(c));
        }
        return folded.ToString();
    }
}
