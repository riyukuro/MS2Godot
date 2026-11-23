using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G17 — individual drop boxes (D16): GMS2 replaces individualitemdrop_final.xml with twelve flat files; box 310000070 in
/// table/individualitemdrop.xml holds 30000063, 30000064, 39000001, ... IndividualDropTable reads the final file only.
/// KMS box 20001001 group 1 keeps item 30000448 ×5.
/// </summary>
public class RcG17IndividualDropTests
{
    [Test]
    public void TheKmsDropBoxKeepsItsItems()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new IndividualDropTable(packs);
        Assert.That(t.Has(20001001), Is.True, "kms table/individualitemdrop_final.xml");
        var list = new List<ItemDropEntry>();
        Assert.That(t.GetBoxItems(new ItemDataTable(packs), 20001001, 10, 0, false, list), Is.True);
        Assert.That(list, Does.Contain(new ItemDropEntry(30000448, 5, 0, 0, 0)));
    }

    [Test]
    [Ignore("G17: IndividualDropTable reads table/individualitemdrop_final.xml only; the twelve GMS2 files are unread")]
    public void TheGms2DropBoxReadsItsFlatRows()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new IndividualDropTable(packs);
        Assert.That(t.Has(310000070), Is.True, "gms table/individualitemdrop.xml");
        var list = new List<ItemDropEntry>();
        Assert.That(t.GetBoxItems(new ItemDataTable(packs), 310000070, 10, 0, false, list), Is.True);
        Assert.That(list.Select(e => e.ItemId), Does.Contain(30000063));
        Assert.That(list.Select(e => e.ItemId), Does.Contain(30000064));
    }
}
