using System.Globalization;
using System.Text;

namespace Pipeline.Core.Articles;

/// <summary>
/// Search form of a title: lower case without accents, Turkish letters mapped to their plain forms
/// ("İŞÇİ" and "isci" both become "isci"). Stored next to the title and applied to the search text,
/// so a plain substring match finds titles however they were typed.
/// </summary>
public static class TitleSearch
{
    public const int MinLength = 2;

    public static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        var space = false;
        // Decomposed, "ş" is "s" + cedilla and "İ" is "I" + dot; the marks are dropped. Dotless "ı" has no mark.
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                space = folded.Length > 0;
                continue;
            }
            if (space)
            {
                folded.Append(' ');
                space = false;
            }
            folded.Append(c switch
            {
                'ı' or 'I' => 'i',
                '’' or '‘' => '\'',
                _ => char.ToLowerInvariant(c),
            });
        }
        return folded.ToString();
    }
}
