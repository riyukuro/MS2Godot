using System.Reflection;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G03 — drop look, drop sound, type colour (D17): GMS2 table/itemdata.xml has 20 &lt;dropitem name=... type ...&gt; rows
/// (equip_ItemBox is type 2) and item/1/12/11200001.xml &lt;drop dropDisplay="equip_ItemBox"&gt;; ItemDropTable reads only
/// the KMS &lt;dropStyle&gt;/&lt;dropItemAsset&gt;. KMS Look(90000001, 20) is style 12 (meso).
/// </summary>
public class RcG03DropLookTests
{
    static string StyleName(DropLook look)
    {
        foreach (var o in new object[] { look, look.Style })
        {
            var t = o.GetType();
            var p = t.GetProperty("StyleName", BindingFlags.Public | BindingFlags.Instance);
            if (p != null) return (string)p.GetValue(o);
            var f = t.GetField("StyleName", BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return (string)f.GetValue(o);
        }
        Assert.Fail("G03: DropStyle/DropLook has no StyleName yet (the data gap notes)");
        return null;
    }

    [Test]
    public void TheKmsMesoStillTakesItsDropRow()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var look = new ItemDropTable(packs).Look(0x55d4a81, 20, 3, false);
        Assert.That(look, Is.Not.Null, "kms table/itemdata.xml dropItemAsset 90000001 min 20");
        Assert.That(look.Style.TypeId, Is.EqualTo(12));
        Assert.That(look.IsMeso, Is.True);
    }

    [Test]
    [Ignore("G03: ItemDropTable reads dropStyle/dropItemAsset only; the GMS2 dropitem/drop/dropDisplay and itemsound rows are unread")]
    public void TheGms2ItemTakesItsNamedDropStyle()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var item = new ItemDataTable(packs).Get(11200001);
        Assert.That(item, Is.Not.Null, "gms/item/1/12/11200001.xml");
        var look = typeof(ItemDropTable)
            .GetMethod("Look", new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(ItemData) });
        Assert.That(look, Is.Not.Null, "G03: the ItemData overload of ItemDropTable.Look does not exist yet");
        var drop = (DropLook)look.Invoke(new ItemDropTable(packs), new object[] { 11200001, 1, 3, false, item });
        Assert.That(drop, Is.Not.Null);
        Assert.That(drop.Style.TypeId, Is.EqualTo(2), "dropitem equip_ItemBox type=\"2\"");
        Assert.That(StyleName(drop), Is.EqualTo("equip_ItemBox"));
        Assert.That(drop.Asset, Is.EqualTo("Item_Box_A01"));
    }
}
