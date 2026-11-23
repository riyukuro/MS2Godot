using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G02 — set bonuses in table/setitemoption.xml (D14): GMS2 table/setiteminfo.xml set 15123101 has optionID="15123101" and
/// no &lt;part&gt;; the parts moved to table/setitemoption.xml &lt;option id="15123101"&gt; with str_value_base="9",
/// pap_value_base="6" and pen_rate_base="0.02" (→20 per-mille). KMS table/setiteminfo.xml carries the same values in
/// its own &lt;part count str="9" pap="6" pen="20"&gt; rows.
/// </summary>
public class RcG02SetBonusTests
{
    [Test]
    public void TheKmsSetKeepsItsParts()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var set = new GameTables(packs, "en").SetItems.Get(15123101);
        Assert.That(set, Is.Not.Null, "kms table/setiteminfo.xml set 15123101");
        Assert.That(set.Parts, Does.ContainKey(2));
        Assert.That(set.Parts, Does.ContainKey(4));
        Assert.That(set.Parts, Does.ContainKey(5));
        Assert.That(set.Parts[2].Stats[0], Is.EqualTo(9), "str=9");
        Assert.That(set.Parts[4].Stats[23], Is.EqualTo(6), "pap=6");
        Assert.That(set.Parts[5].Stats[31], Is.EqualTo(20), "pen=20");
    }

    [Test]
    [Ignore("G02: SetItemTable reads only setiteminfo/set/part; GMS2 moved the parts to table/setitemoption.xml")]
    public void TheGms2SetReadsItsOptionParts()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var set = new GameTables(packs, "en").SetItems.Get(15123101);
        Assert.That(set, Is.Not.Null, "gms table/setiteminfo.xml set 15123101");
        Assert.That(set.Parts, Does.ContainKey(2), "gms table/setitemoption.xml option 15123101 part count 2");
        Assert.That(set.Parts, Does.ContainKey(4));
        Assert.That(set.Parts, Does.ContainKey(5));
        Assert.That(set.Parts[2].Stats[0], Is.EqualTo(9), "str_value_base=\"9\"");
        Assert.That(set.Parts[4].Stats[23], Is.EqualTo(6), "pap_value_base=\"6\"");
        Assert.That(set.Parts[5].Stats[31], Is.EqualTo(20), "pen_rate_base=\"0.02\" → 20");
    }
}
