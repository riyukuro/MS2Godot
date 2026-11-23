using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G05 — NPC &lt;effectdummy&gt; (D4): GMS2 puts it as a sibling of &lt;environment&gt; under &lt;ms2&gt;; PerFileTable.Build
/// copies only the environment, so NpcTable's MergeNpc never sees the 8 dummies. Fixtures: gms/npc/22/00/22000052.xml
/// (Eff_Head on Bip01 Head, scale 0.9, plus Eff_UI) and kms/npcdata/220.xml (inside the npc row's environment).
/// </summary>
public class RcG05NpcEffectDummyTests
{
    [Test]
    public void TheKmsNpcKeepsItsEffectDummies()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var d = new NpcTable(packs).Find(22000052)?.Dummy("Eff_Head");
        Assert.That(d, Is.Not.Null);
        Assert.That((d.ParentNode, d.Scale), Is.EqualTo(("Bip01 Head", 0.9f)));
    }

    [Test]
    [Ignore("G05: GMS2 ms2/effectdummy is dropped by PerFileTable.Build")]
    public void TheGms2NpcReadsItsEightDummies()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var row = new NpcTable(packs).Find(22000052);
        Assert.That(row, Is.Not.Null, "gms/npc/22/00/22000052.xml npc 22000052");
        var head = row.Dummy("Eff_Head");
        Assert.That(head, Is.Not.Null, "the sibling <effectdummy> has Eff_Head");
        Assert.That((head.ParentNode, head.Scale), Is.EqualTo(("Bip01 Head", 0.9f)));
        Assert.That(row.Dummy("Eff_UI"), Is.Not.Null);
        Assert.That(row.EffectDummies.Count, Is.GreaterThanOrEqualTo(8), "the file has 8 dummies, not only the defaults");
    }
}
