using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The client's string folder pick: SetLanguage's g_strLocaleDir2 switch and SetLanguageByCode (localepath.cpp).</summary>
public class GameOptionLocaleTests
{
    [TestCase(0, "kr")]
    [TestCase(1, "jp")]
    [TestCase(2, "en")]
    [TestCase(3, "cn")]
    [TestCase(4, "fr")]
    [TestCase(5, "de")]
    [TestCase(6, "pt")]
    [TestCase(7, "es")]
    [TestCase(8, "kr")]
    [TestCase(-1, "kr")]
    public void StringDirectoryFollowsSetLanguage(int language, string folder) =>
        Assert.That(GameOptionLocale.StringDirectory(language), Is.EqualTo(folder));

    [Test]
    public void LanguageByCodeUpperCasesAndKeepsUnknown()
    {
        Assert.That(GameOptionLocale.LanguageByCode("en", 0), Is.EqualTo(2));
        Assert.That(GameOptionLocale.LanguageByCode("DE", 0), Is.EqualTo(5));
        Assert.That(GameOptionLocale.LanguageByCode("fr", 0), Is.EqualTo(4), "SetLanguageByCode compares FUN_140652030's \"FR\", not GetLanguageId's \"FR)\"");
        Assert.That(GameOptionLocale.LanguageByCode("xx", 3), Is.EqualTo(3));
        Assert.That(GameOptionLocale.LanguageByCode(null, 1), Is.EqualTo(1));
    }

    [Test]
    public void RegionDefaultIsKoreanFolder()
    {
        // the Unk_LgState reset: language = region index; KR (0) -> "kr"
        Assert.That(GameOptionLocale.StringDirectory(new GameOption.Language(0).LanguageIndex), Is.EqualTo("kr"));
    }
}
