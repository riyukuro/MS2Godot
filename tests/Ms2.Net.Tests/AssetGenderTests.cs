using System.Xml.Linq;
using Ms2Godot.Net;
using NUnit.Framework;
using Ms2.Formats.World;
using Ms2.Formats.Pack;

namespace Ms2.Net.Tests;

public class AssetGenderTests
{
    [TestCase(null, 0, true)]
    [TestCase(null, 1, true)]
    [TestCase("2", 0, true)]
    [TestCase("2", 1, true)]
    [TestCase("0", 0, true)]
    [TestCase("0", 1, false)]
    [TestCase("1", 1, true)]
    [TestCase("1", 0, false)]
    public void AnAssetIsWornWhenItsGenderIsBothOrTheCharacters(string attr, int gender, bool worn)
        => Assert.That(AssetGender.Matches(attr, gender), Is.EqualTo(worn));   // pc.cpp:1226

    [Test]
    public void TheGms2HairPresetIsPickedForBothGenders()
    {
        string dir = System.Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(dir)) Assert.Ignore("no MS2_DATA");
        using var packs = new PackSet(dir);
        if (!packs.Exists("Xml", "itempreset/1/02/10200001.xml")) Assert.Ignore("MS2_DATA has no itempreset files");
        var doc = PerFileTable.ItemModels(packs, 102);
        var hair = doc.Root.Elements("ItemModel").First(e => (string)e.Attribute("id") == "10200001");
        var assets = hair.Element("slots").Element("slot").Elements("asset").ToList();
        foreach (int g in new[] { 0, 1 })
            Assert.That(assets.Any(a => AssetGender.Matches((string)a.Attribute("gender"), g)), Is.True, $"gender {g}");
    }
}
