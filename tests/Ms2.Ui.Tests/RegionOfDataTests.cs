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
}
