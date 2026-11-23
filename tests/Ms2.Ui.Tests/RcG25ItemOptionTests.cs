using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G25 — item options split into 72 files (D13): GMS2 keeps itemoption/constant/itemoptionconstant_equip.xml with
/// &lt;option code="11200001" grade="3" mar_value_base="4"&gt; and itemoption/option/random/itemoptionrandom_12.xml with
/// optionNumPick="2,2" at grade 3; ItemOptionTables reads table/itemoptionconstant.xml/itemoptionrandom.xml, absent on
/// GMS2. KMS table/itemoptionconstant.xml code 11200006 keeps hp=39.
/// </summary>
public class RcG25ItemOptionTests
{
    [Test]
    public void TheKmsConstantOptionsAreUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var g = new GameTables(packs, "en");
        var item = g.Items.Get(11200006);
        Assert.That(item, Is.Not.Null, "kms itemdata/112.xml");
        var c = g.ItemOptions.ConstantOptions(item, 4);
        Assert.That(c.HasValue, Is.True);
        Assert.That(c.Value.Stat, Does.ContainKey(g.ItemOptions.FindStatByName("hp")), "kms code 11200006 <v name=\"hp\" value=\"39\">");
        Assert.That(c.Value.Stat[g.ItemOptions.FindStatByName("hp")], Is.EqualTo(39));
    }

    [Test]
    [Ignore("G25: ItemOptionTables reads table/itemoption{random,constant,variation}.xml only; the GMS2 itemoption/ and itemoptionpick tables are unread")]
    public void TheGms2ConstantAndRandomOptionsLoad()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        var item = g.Items.Get(11200001);
        Assert.That(item, Is.Not.Null, "gms/item/1/12/11200001.xml");
        int mar = g.ItemOptions.FindStatByName("mar");
        var c = g.ItemOptions.ConstantOptions(item, 3);
        Assert.That(c.HasValue, Is.True);
        Assert.That(c.Value.Stat, Does.ContainKey(mar), "gms itemoptionconstant_equip.xml code 11200001 grade 3 mar_value_base=4");
        Assert.That(c.Value.Stat[mar], Is.EqualTo(4));
        Assert.That(g.ItemOptions.PickCount(item, 3), Is.EqualTo(2), "itemoptionrandom_12.xml optionNumPick=\"2,2\" at grade 3");
    }
}
