using Pipeline.Core.Articles;

namespace Pipeline.Core.Tests.Articles;

public class TitleSearchTests
{
    [Theory]
    [InlineData("İŞÇİ", "isci")]
    [InlineData("işçi", "isci")]
    [InlineData("ISCI", "isci")]
    [InlineData("Işık Tekstil’de grev", "isik tekstil'de grev")]
    [InlineData("ĞÜÖÇŞ ğüöçş", "guocs guocs")]
    [InlineData("  iki   boşluk\n", "iki bosluk")]
    public void FoldsCaseAccentsAndTurkishLetters(string text, string folded) =>
        Assert.Equal(folded, TitleSearch.Fold(text));
}
