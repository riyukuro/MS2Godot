using Ms2.Formats.Pack;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>FUN_14028faa0's row pick (FeatureElements.Collect) over rows a reader took: the short form and the keyed form
/// give the same rows in the same order.</summary>
public class FeatureElementsTests
{
    sealed record Row(string Key, string Feature, string Locale);

    static List<Row> Pick(PackSet packs, IEnumerable<Row> rows) => FeatureElements.Collect(packs, rows, r => r.Key, r => r.Feature, r => r.Locale);

    [Test]
    public void TheShortAndTheKeyedPickAgree()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-feature-rows-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Resource"));
            var table = Path.Combine(dir, "Xml", "table");
            Directory.CreateDirectory(table);
            File.WriteAllText(Path.Combine(table, "feature_setting.xml"), "<ms2><setting type=\"Live\" KR=\"30\"/></ms2>");
            File.WriteAllText(Path.Combine(table, "feature.xml"),
                "<ms2><feature name=\"Low\" KR=\"5\"/><feature name=\"High\" KR=\"9\"/><feature name=\"Off\" KR=\"31\"/></ms2>");
            var packs = new PackSet(dir);
            Row plain1 = new("1", "", ""), high2 = new("2", "High", ""), off3 = new("3", "Off", ""), low1 = new("1", "Low", ""),
                other4 = new("4", "", "XX"), plain5 = new("5", "", ""), low2 = new("2", "Low", ""),
                own5 = new("5", "", LobbyFeatures.LocaleOf(packs)), low6 = new("6", "Low", ""), low6b = new("6", "Low", "");
            var rows = new List<Row> { plain1, high2, off3, low1, other4, plain5, low2, own5, low6, low6b };
            var expected = new[] { high2, low1, own5, low6 };   // a replacing row takes the replaced one's place at its own position
            Assert.That(Pick(packs, rows), Is.EqualTo(expected).AsCollection, "short");
            Assert.That(Pick(packs, rows.Select(r => r)), Is.EqualTo(expected).AsCollection, "keyed");
            Assert.That(Pick(packs, rows)[3], Is.SameAs(low6), "the same feature and locale keep the first");
            // past the short form's count both forms take the keyed one's answer
            var many = new List<Row>(rows);
            for (int i = 0; i <= FeatureElements.ShortRows; i++) many.Add(new Row("k" + i % 7, i % 3 == 0 ? "Low" : "", i % 5 == 0 ? LobbyFeatures.LocaleOf(packs) : ""));
            Assert.That(Pick(packs, many), Is.EqualTo(Pick(packs, many.Select(r => r))).AsCollection);
            Assert.That(Pick(packs, many.Take(FeatureElements.ShortRows).ToList()), Is.EqualTo(Pick(packs, many.Take(FeatureElements.ShortRows))).AsCollection);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void TheLocaleIsTheDataRegion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-locale-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Resource"));
            var table = Path.Combine(dir, "Xml", "table");
            Directory.CreateDirectory(Path.Combine(table, "na"));
            File.WriteAllText(Path.Combine(table, "na", "multicontrol.xml"), "<ms2/>");
            File.WriteAllText(Path.Combine(table, "feature_setting.xml"), "<ms2><setting type=\"Live\" NA=\"47\" KR=\"30\"/></ms2>");
            File.WriteAllText(Path.Combine(table, "feature.xml"),
                "<ms2><feature name=\"OnNa\" NA=\"1\" KR=\"100\"/><feature name=\"OffNa\" NA=\"50\" KR=\"1\"/></ms2>");
            var packs = new PackSet(dir);
            Assert.That(LobbyFeatures.LocaleOf(packs), Is.EqualTo("NA"));
            Assert.That(LobbyFeatures.IsEnabled(packs, "OnNa"), Is.True);
            Assert.That(LobbyFeatures.IsEnabled(packs, "OffNa"), Is.False);
            Assert.That(LobbyFeatures.IsLocaleInList(packs, "KR,NA"), Is.True);
            Assert.That(LobbyFeatures.IsLocaleInList(packs, "KR"), Is.False);
            Row own = new("1", "", "NA"), kr = new("1", "", "KR"), plain = new("1", "", "");
            Assert.That(Pick(packs, new[] { plain, own }), Is.EqualTo(new[] { own }).AsCollection);
            Assert.That(Pick(packs, new[] { kr }), Is.Empty);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void NoRegionFolderIsKr()
    {
        Assert.That(LobbyFeatures.LocaleOf(null), Is.EqualTo("KR"));
        Assert.That(TablePath.Region(_ => false), Is.EqualTo("kr"));
    }
}
