using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The item and model rows the character create page reads (table/defaultitems.xml lists the items each slot offers):
/// found in the KMS files where the pack has them, else in the GMS2 per-item and preset files. Needs MS2_DATA.</summary>
public class ItemFilesTests
{
    static PackSet Packs()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new PackSet(d);
    }

    [Test]
    public void EveryVisibleDefaultItemHasItsDataAndItsModelSlot()
    {
        var packs = Packs();
        var doc = PackXml.Parse(packs.Read("Xml", "table/defaultitems.xml"));
        var files = new ItemFiles(packs);
        int seen = 0, noData = 0, noSlot = 0;
        foreach (var it in doc.Root.Descendants("item").Where(i => (string)i.Attribute("visible") == "1"))
        {
            if (!int.TryParse((string)it.Attribute("id"), out int id)) continue;
            seen++;
            var data = files.Item(id);
            if (data == null) { noData++; continue; }
            int model = int.TryParse((string)data.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int pre) && pre != 0 ? pre : id;
            if (files.Model(model)?.Element("slots")?.Element("slot")?.Attribute("name") == null) noSlot++;
        }
        Assert.That(seen, Is.GreaterThan(0));
        Assert.That((noData, noSlot), Is.EqualTo((0, 0)), $"of {seen} visible default items");
    }
}
