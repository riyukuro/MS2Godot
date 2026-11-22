using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The service region is the data's: KR on the KMS2 packs, NA (region 2, English strings) on the GMS2 packs.</summary>
public class RegionOfDataTests
{
    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    [Test]
    public void TheRegionFollowsTheDataLocale()
    {
        var packs = Packs();
        bool na = LobbyFeatures.LocaleOf(packs) == "NA";
        Assert.That(LobbyFeatures.RegionOf(packs), Is.EqualTo(na ? 2 : 0));
        // the region's language reset (Unk_LgState): NA is EN, KR is KR
        var lang = new GameOption.Language(LobbyFeatures.RegionOf(packs));
        Assert.That(GameOptionLocale.StringDirectory(lang.LanguageIndex), Is.EqualTo(na ? "en" : "kr"));
    }

    [Test]
    public void NoDataIsKr()
    {
        Assert.That(LobbyFeatures.RegionOf(null), Is.Zero);
    }

    static int RegionOfLooseFolder(string folder)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-region-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(dir, "Xml", "table", folder, "multicontrol.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Directory.CreateDirectory(Path.Combine(dir, "Resource"));
            File.WriteAllText(file, "<ms2/>");
            using var packs = new PackSet(dir);
            return LobbyFeatures.RegionOf(packs);
        }
        finally { Directory.Delete(dir, true); }
    }

    // data-free: a loose table folder stands for the pack's, as the client reads loose files first
    [Test]
    public void ANaTableFolderMakesRegion2AndAKrFolderRegion0()
    {
        Assert.That(RegionOfLooseFolder("na"), Is.EqualTo(2));
        Assert.That(RegionOfLooseFolder("kr"), Is.EqualTo(0));
    }

    [Test]
    public void TheNaRegionPicksTheLanguageOfTheSystemLocale()
    {
        // the fresh-file pick of the NA region (gameoption.cpp:1840-1844)
        Assert.That(GameOptionLocale.LanguageFromLangId(GameOptionLocale.WindowsLangId("de_DE.UTF-8")), Is.EqualTo(5));
        Assert.That(GameOptionLocale.LanguageFromLangId(GameOptionLocale.WindowsLangId("pt_BR")), Is.EqualTo(6));
        Assert.That(GameOptionLocale.LanguageFromLangId(GameOptionLocale.WindowsLangId("en_US")), Is.EqualTo(2));
        Assert.That(new GameOption.Language(2).LanguageIndex, Is.EqualTo(2));
    }
}
