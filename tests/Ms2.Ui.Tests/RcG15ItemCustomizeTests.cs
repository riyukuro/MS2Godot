using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G15 — item customize HR/FD/CP (D10): the reader is game/ui/create/ItemCustomize.cs, which no test project references
/// (it takes Godot types). Its fixture is asserted here: GMS2 itempreset/1/02/10200001.xml &lt;customize&gt; holds the
/// flags as CHILDREN — HR scale="1" pony="0", FD translation/rotation/scale="0", CP xrotation/yrotation/zrotation/scale
/// ="0" with a &lt;transform&gt; preset — and the slot's one &lt;scale value="0.2,0.4,0.6,0.8,1"&gt;. KMS itemmodel/102.xml
/// 10200001 keeps customize@scale="1".
/// </summary>
public class RcG15ItemCustomizeTests
{
    static XElement Model(PackSet packs, int preset) =>
        PerFileTable.ItemModels(packs, preset / 100000)?.Root.Elements("ItemModel")
            .FirstOrDefault(e => (string)e.Attribute("id") == preset.ToString());

    [Test]
    public void TheKmsModelKeepsItsCustomizeScaleAttribute()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var doc = GameStrings.Parse(packs.Read("Xml", "itemmodel/102.xml"));
        var cust = doc.Root.Elements("ItemModel").First(e => (string)e.Attribute("id") == "10200001").Element("customize");
        Assert.That(cust, Is.Not.Null);
        Assert.That((string)cust.Attribute("scale"), Is.EqualTo("1"));
    }

    [Test]
    [Ignore("G15: the reader is game/ui/create/ItemCustomize.cs (Godot), referenced by no test project; the GMS2 preset shape is asserted only")]
    public void TheGms2PresetCarriesTheChildCustomizeFlags()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var model = Model(packs, 10200001);
        Assert.That(model, Is.Not.Null, "gms/itempreset/1/02/10200001.xml");
        var cust = model.Element("customize");
        Assert.That(cust, Is.Not.Null);
        var hr = cust.Element("HR");
        Assert.That((string)hr?.Attribute("scale"), Is.EqualTo("1"), "hair can change size");
        Assert.That((string)hr?.Attribute("pony"), Is.EqualTo("0"));
        var fd = cust.Element("FD");
        Assert.That(((string)fd?.Attribute("translation"), (string)fd?.Attribute("rotation"), (string)fd?.Attribute("scale")),
            Is.EqualTo(("0", "0", "0")));
        var cp = cust.Element("CP");
        Assert.That(((string)cp?.Attribute("xrotation"), (string)cp?.Attribute("yrotation"), (string)cp?.Attribute("zrotation"), (string)cp?.Attribute("scale")),
            Is.EqualTo(("0", "0", "0", "0")));
        var scale = model.Element("slots")?.Elements("slot").First(s => (string)s.Attribute("name") == "HR")?.Element("scale");
        Assert.That((string)scale?.Attribute("value"), Is.EqualTo("0.200000,0.400000,0.600000,0.800000,1.000000"));
    }
}
