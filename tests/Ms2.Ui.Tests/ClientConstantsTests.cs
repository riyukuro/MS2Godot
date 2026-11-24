using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class ClientConstantsTests
{
    /// <summary>
    /// table/constants.xml has a KR row, then a row without a locale, for each of the UGC shop's character rotate
    /// angles. FUN_14028faa0 keeps the first row of a key and takes a later one only when SelectFeatureElement picks it;
    /// with the same feature, FUN_14036dfa0 prefers the client's locale, so on KR data the KR row stays (0 and 360) and on
    /// data of another locale (GMS2: NA) the row without one is the only row left (178 and 8).
    /// </summary>
    [Test]
    public void TheLocaleRowWinsOverTheRowWithoutOne()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var k = FeatureElements.Constants(packs);
        foreach (var key in new[] { "UgcshopCharRotateStartDegreeY", "UgcshopCharRotateEndDegreeY" })
        {
            Assert.That(Rows(packs, key).Count(e => (string)e.Attribute("locale") is null), Is.EqualTo(1), key + ": the row without a locale");
            Assert.That(k.Float(key, -1), Is.EqualTo(float.Parse(TableValue(packs, key), CultureInfo.InvariantCulture)), key);
        }
    }

    /// <summary>The rows of table/constants.xml for <paramref name="key"/> that IsElementAvailable passes in this data.</summary>
    static List<XElement> Rows(PackSet packs, string key) =>
        GameStrings.Parse(packs.Read("Xml", "table/constants.xml")).Root!.Elements("v")
            .Where(e => (string)e.Attribute("key") == key && FeatureElements.IsAvailable(packs, e)).ToList();

    /// <summary>The value the data's own table gives <paramref name="key"/>: the available row of the client's locale, else the
    /// one available row without a locale; null when no row is available.</summary>
    internal static string TableValue(PackSet packs, string key)
    {
        var rows = Rows(packs, key);
        var local = rows.Where(e => !string.IsNullOrEmpty((string)e.Attribute("locale"))).ToList();
        Assert.That(local, Has.Count.LessThanOrEqualTo(1), key + ": one row of the locale");
        if (local.Count == 1) return (string)local[0].Attribute("value");
        Assert.That(rows, Has.Count.LessThanOrEqualTo(1), key + ": one row without a locale");
        return rows.Count == 1 ? (string)rows[0].Attribute("value") : null;
    }
}
