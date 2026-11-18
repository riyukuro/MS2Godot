using Ms2.Formats.Pack;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class ClientConstantsTests
{
    /// <summary>
    /// table/constants.xml has a KR row, then a row without a locale, for each of the UGC shop's character rotate
    /// angles. FUN_14028faa0 keeps the first row of a key and takes a later one only when SelectFeatureElement picks it;
    /// with the same feature, FUN_14036dfa0 prefers the client's locale, so the KR row stays.
    /// </summary>
    [Test]
    public void TheLocaleRowWinsOverTheRowWithoutOne()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var k = FeatureElements.Constants(packs);
        Assert.That(k.Float("UgcshopCharRotateStartDegreeY", -1), Is.EqualTo(0f));
        Assert.That(k.Float("UgcshopCharRotateEndDegreeY", -1), Is.EqualTo(360f));
    }
}
