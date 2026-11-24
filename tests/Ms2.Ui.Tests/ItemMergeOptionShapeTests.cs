using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CItemMergeOptionTable on the GMS2 data's shape: &lt;mergeStep&gt; around the slots, idxN="min,max" with
/// "_value_base" / "_rate_base" names, and the materials keyed by their own id (subtype 21 by part, 22 in
/// itemMergeOptionMaterial.xml).</summary>
[NonParallelizable]
public class ItemMergeOptionShapeTests
{
    static readonly Dictionary<string, int> Stats = new() { ["pap"] = 4, ["pen"] = 9 };
    static int Stat(string n) => Stats.TryGetValue(n, out int v) ? v : -1;
    static int Special(string n) => n == "sgi_boss" ? 21 : 0;
    static IEnumerable<XElement> All(XElement p, string c, string k) => p.Elements(c);

    static ItemData Equip(int subtype) => new() { Type = 1, Subtype = subtype };
    static ItemData Material(int id, int subtype, int param1 = 0) => new() { Id = id, Type = 0x13, Subtype = subtype, Param1 = param1 };

    [Test]
    public void TheGms2StepsAreReadAsRangesAndTheRatesInThousandths()
    {
        var doc = XDocument.Parse("""
            <ms2><mergeOption id="37000001" feature="" locale=""><mergeStep step="1"><slot part="13" partLimit="" consumeMeso="1">
              <option optionName="pap_value_base" isStaticOption="0" idx0="3,4" idx1="5,6" idx2="7,7" idx3="" idx4="9,9"/>
              <option optionName="pen_rate_base" isStaticOption="0" idx0="0.008,0.012" idx1="0.013,0.017" idx2=""/>
              <option optionName="sgi_boss_rate_base" isStaticOption="0" idx0="" idx1=""/>
            </slot></mergeStep></mergeOption></ms2>
            """);
        var mat = XDocument.Parse("""
            <ms2><mergeOption id="37030001" feature=""><mergeStep step="1"><slot part="" partLimit="13,14" consumeMeso="1">
              <option optionName="pap_value_base" isStaticOption="0" idx0="1,2" idx1="3,4" idx2="5,6" idx3="7,8" idx4="9,10"/>
            </slot></mergeStep></mergeOption></ms2>
            """);
        var t = new ItemMergeOptionTable(doc, All, Stat, Special, mat);
        var slot = t.Find(Equip(13), Material(37000001, 21));
        Assert.That(slot, Is.Not.Null);
        // three steps up to the first empty one: the whole range 3..7, the top range from step n - 1 (n <= 4)
        Assert.That(slot[0], Is.EqualTo(new ItemMergeOptionTable.Option(4, 0, 3, 7, 7, 7)));
        // a rate: 0.008 .. 0.017 is 8 .. 17, the KMS data's unit
        Assert.That(slot[1], Is.EqualTo(new ItemMergeOptionTable.Option(9, 0, 8, 17, 13, 17)));
        // a special without steps
        Assert.That(slot[2], Is.EqualTo(new ItemMergeOptionTable.Option(-1, 21, 0, 0, 0, 0)));
        Assert.That(t.Find(Equip(14), Material(37000001, 21)), Is.Null, "no slot for that part");
        // a subtype 22 material: its own id in the material table, one slot without a part
        var m = t.Find(Equip(14), Material(37030001, 22));
        Assert.That(m, Is.Not.Null);
        Assert.That(m[0], Is.EqualTo(new ItemMergeOptionTable.Option(4, 0, 1, 10, 7, 10)));
        Assert.That(t.Find(Equip(13), Material(37000001, 22)), Is.Null, "a subtype 22 id is looked up in the material table");
        // a KMS material still goes by its param1 into the base table
        Assert.That(t.Find(Equip(13), Material(37000017, 1, 37000001)), Is.SameAs(slot));
        Assert.That(t.Find(Equip(13), Material(37000001, 1)), Is.Null, "a KMS subtype 1 material without param1 has no option");
    }

    /// <summary>
    /// The GMS2 data, whole list: every merge material (type 19) is subtype 21 or 22 without a param1; every available
    /// mergeOption slot of both tables loads (each option name is a stat or a special), and its material finds it with the
    /// ranges of the file's idxN. Before, nothing loaded (the slots sit in &lt;mergeStep&gt; and the steps are idxN, not idxN_max).
    /// </summary>
    [Test]
    public void EveryGms2MergeMaterialFindsItsSlots()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        using var packs = new PackSet(d);
        var t = new GameTables(packs, "en");
        int slots = 0, options = 0, rates = 0;
        foreach (var (file, subtype) in new[] { ("itemmergeoptionbase.xml", 21), (ItemMergeOptionTable.MaterialFile, 22) })
        {
            var entry = TablePath.Resolve(packs, file);
            Assert.That(entry, Is.Not.Null, file);
            var root = GameStrings.Parse(packs.Read("Xml", entry)).Root!;
            foreach (var node in FeatureElements.Collect(packs, root, "mergeOption", "id"))
            {
                int id = int.Parse((string)node.Attribute("id")!, CultureInfo.InvariantCulture);
                var material = t.Items.Get(id);
                Assert.That(material, Is.Not.Null, $"material {id}");
                Assert.That((material.Type, material.Subtype, material.Param1), Is.EqualTo((0x13, subtype, 0)), $"material {id}");
                foreach (var slot in node.Elements("mergeStep").Elements("slot"))
                {
                    int.TryParse((string)slot.Attribute("part"), out int part);
                    var found = t.MergeOptions.Find(Equip(part), material);
                    Assert.That(found, Is.Not.Null, $"{file} {id} part {part}");
                    var xs = slot.Elements("option").ToList();
                    Assert.That(found, Has.Count.EqualTo(xs.Count), $"{file} {id} part {part}");
                    slots++;
                    for (int i = 0; i < xs.Count; i++)
                    {
                        string name = (string)xs[i].Attribute("optionName")!;
                        bool rate = name.EndsWith("_rate_base", StringComparison.Ordinal);
                        var steps = Enumerable.Range(0, 10).Select(n => (string)xs[i].Attribute("idx" + n) ?? "").TakeWhile(v => v.Length != 0)
                            .Select(v => v.Split(',').Select(x => (int)Math.Round(decimal.Parse(x, CultureInfo.InvariantCulture) * (rate ? 1000 : 1))).ToArray()).ToList();
                        var o = found[i];
                        Assert.That(o.Stat != -1 || o.Special != 0, Is.True, $"{id} {name}");
                        var expect = steps.Count == 0 ? (0, 0) : (steps[0][0], steps[^1][1]);
                        Assert.That((o.FirstMin, o.FirstMax), Is.EqualTo(expect), $"{file} {id} part {part} {name}");
                        options++;
                        if (rate) rates++;
                    }
                }
            }
        }
        TestContext.Out.WriteLine($"{slots} slots, {options} options ({rates} rates)");
        Assert.That(slots, Is.GreaterThan(100));
        Assert.That(rates, Is.GreaterThan(100));
    }
}
