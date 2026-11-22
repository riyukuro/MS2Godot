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
    public void ASavedLanguageWithoutStringsGivesTheRegionsOwn()
    {
        var gms2 = new HashSet<int> { 2, 5, 6 };   // the GMS2 data holds string/en, de and pt
        Assert.That(GameOptionLocale.LanguageWithStrings(0, 2, gms2.Contains), Is.EqualTo(2), "a KMS2 run's index 0 is string/kr");
        Assert.That(GameOptionLocale.LanguageWithStrings(5, 2, gms2.Contains), Is.EqualTo(5));
        Assert.That(GameOptionLocale.LanguageWithStrings(3, 0, gms2.Contains), Is.EqualTo(0), "no strings: the region's, whatever the saved index");
        Assert.That(GameOptionLocale.LanguageWithStrings(7, 2, _ => false), Is.EqualTo(2), "a data folder with no string folder at all");
    }

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

    [Test]
    public void TheNaRegionPicksItsLanguageFromTheUiLanguage()
    {
        Assert.That(GameOptionLocale.LanguageFromLangId(0x407), Is.EqualTo(5));
        Assert.That(GameOptionLocale.LanguageFromLangId(0x416), Is.EqualTo(6));
        Assert.That(GameOptionLocale.LanguageFromLangId(0x409), Is.EqualTo(2));
        Assert.That(GameOptionLocale.LanguageFromLangId(0x412), Is.EqualTo(2));
        Assert.That(GameOptionLocale.WindowsLangId("de_DE.UTF-8"), Is.EqualTo(0x407));
        Assert.That(GameOptionLocale.WindowsLangId("pt-BR"), Is.EqualTo(0x416));
        Assert.That(GameOptionLocale.WindowsLangId("ko_KR"), Is.Zero);
        Assert.That(GameOptionLocale.WindowsLangId(null), Is.Zero);
    }
}
