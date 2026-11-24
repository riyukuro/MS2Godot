using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 data's boxes: the type 10 box kinds, the OpenItemBox parameter and the flat individual drop rows.</summary>
[NonParallelizable]
public class Gms2BoxTests
{
    static ItemData Load(string env)
    {
        var d = ItemData.Load(1, XElement.Parse(env));
        d.FillBoxType();
        return d;
    }

    [Test]
    public void TheGms2BoxKindsAreTheKmsTypes()
    {
        Assert.That(Load("<environment><property type=\"10\" subtype=\"17\"/></environment>") is { Type: 0x19, Subtype: 0 }, Is.True);
        Assert.That(Load("<environment><property type=\"10\" subtype=\"18\"/></environment>") is { Type: 0x1a, Subtype: 0 }, Is.True);
        Assert.That(Load("<environment><property type=\"10\" subtype=\"0\"/></environment>") is { Type: 10, Subtype: 0 }, Is.True);
        Assert.That(Load("<environment><property type=\"10\" subtype=\"20\"/></environment>") is { Type: 10, Subtype: 20 }, Is.True);
        Assert.That(Load("<environment><property type=\"19\" subtype=\"17\"/></environment>") is { Type: 19, Subtype: 17 }, Is.True);
    }

    [Test]
    public void TheOpenItemBoxIdIsParam1OrTheIndividualBox()
    {
        Assert.That(Load("<environment><function name=\"OpenItemBox\" param1=\"330000675\"/></environment>").OpenItemBoxId, Is.EqualTo(330000675));
        Assert.That(Load("<environment><function name=\"OpenItemBox\" parameter=\"0,1,0,330000675\"/></environment>").OpenItemBoxId, Is.EqualTo(330000675));
        Assert.That(Load("<environment><function name=\"OpenItemBox\" parameter=\"0,1,0,330000930,10\"/></environment>").OpenItemBoxId, Is.EqualTo(330000930));
        Assert.That(Load("<environment><function name=\"OpenItemBox\" parameter=\"&lt;v globalDropLevel=&quot;0&quot; individualDropBoxId=&quot;330002246&quot;/&gt;\"/></environment>").OpenItemBoxId,
            Is.EqualTo(330002246));
        Assert.That(Load("<environment><function name=\"OpenItemBox\" parameter=\"0,1,30000301,0\"/></environment>").OpenItemBoxId, Is.Zero, "a global box only");
        Assert.That(Load("<environment><function name=\"OpenItemBox\" parameter=\"\"/></environment>").OpenItemBoxId, Is.Zero);
    }

    /// <summary>
    /// The GMS2 data, whole list: every box id of an available flat drop row is a box of the table, holding its available
    /// rows (and item2s) whose item has data; every OpenItemBox item of the item files is type 25 or 26 (or the data's own
    /// type 10 kinds) and, for the type 25 ones, its individual box lists items on the box tooltip's path. Before, the table
    /// had no box on this data (it read only individualItemDrop_Final.xml) and the box items were type 10.
    /// </summary>
    [Test]
    public void EveryGms2DropRowIsInItsBox()
    {
        var dir = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(dir);
        var t = new GameTables(packs, "en");
        var expect = new Dictionary<int, int>();
        var smart = new HashSet<int>();
        int files = 0;
        foreach (var name in packs.List("Xml").Where(n => (n.StartsWith("table/individualitemdrop", StringComparison.Ordinal)
                     || n.StartsWith("table/na/individualitemdrop", StringComparison.Ordinal)) && n.EndsWith(".xml", StringComparison.Ordinal)))
        {
            files++;
            foreach (var e in GameStrings.Parse(packs.Read("Xml", name)).Root.Elements("individualDropBox"))
            {
                if (!FeatureElements.IsAvailable(packs, e)) continue;
                int box = ItemData.Atoi((string)e.Attribute("individualDropBoxID"));
                int n = (t.Items.Get(ItemData.Atoi((string)e.Attribute("item"))) != null ? 1 : 0);
                if (n == 1 && ItemData.Atoi((string)e.Attribute("item2") ?? "0") != 0) n++;
                expect[box] = expect.GetValueOrDefault(box) + n;
                string gender = (string)e.Attribute("isApplySmartGenderDrop") ?? "0";
                if (ItemData.Atoi((string)e.Attribute("smartDropRate") ?? "0") > 0 || gender.Equals("true", StringComparison.OrdinalIgnoreCase) || gender == "1")
                    smart.Add(box);
            }
        }
        Assert.That(files, Is.GreaterThan(5));
        Assert.That(expect.Count, Is.GreaterThan(3000));
        int exact = 0;
        foreach (var (box, n) in expect)
        {
            var got = new List<ItemDropEntry>();
            Assert.That(t.IndividualDrops.GetBoxItems(t.Items, box, 0, 0, false, got), Is.True, $"box {box}");
            // a box whose groups filter by job or gender lists a part of its rows; any other lists them all
            if (smart.Contains(box)) Assert.That(got.Count, Is.LessThanOrEqualTo(n), $"box {box}");
            else { Assert.That(got.Count, Is.EqualTo(n), $"box {box}"); exact++; }
        }
        Assert.That(exact, Is.GreaterThan(1000));
        // every type 25 OpenItemBox item: its box is in the table exactly when the data has an available row of it (490 of
        // the GMS2 box items name a box whose rows are all another locale's), and one with rows and no job or gender
        // filter lists items
        int boxes = 0, withRows = 0, listed = 0;
        foreach (var (id, _) in ItemDataTable.PerItemIndex(packs.List("Xml")))
        {
            var d = t.Items.Get(id);
            if (d?.FunctionName != "OpenItemBox") continue;
            Assert.That(d.Type == 10 && d.Subtype is 17 or 18, Is.False, $"item {id}");
            if (d.Type != 0x19 || d.OpenItemBoxId == 0) continue;
            boxes++;
            Assert.That(t.IndividualDrops.Has(d.OpenItemBoxId), Is.EqualTo(expect.ContainsKey(d.OpenItemBoxId)), $"item {id} box {d.OpenItemBoxId}");
            if (!expect.TryGetValue(d.OpenItemBoxId, out int n) || n == 0) continue;
            withRows++;
            var got = new List<ItemDropEntry>();
            t.IndividualDrops.GetBoxItems(t.Items, d.OpenItemBoxId, 0, 0, false, got);
            if (got.Count > 0 || smart.Contains(d.OpenItemBoxId)) listed++;
        }
        TestContext.Out.WriteLine($"{expect.Count} boxes ({exact} without smart filters) from {files} files; {boxes} type 25 OpenItemBox items, {withRows} with rows");
        Assert.That(boxes, Is.GreaterThan(2000));
        Assert.That(withRows, Is.GreaterThan(1500));
        Assert.That(listed, Is.EqualTo(withRows), "a box with rows and no smart filter lists them");
    }
}
