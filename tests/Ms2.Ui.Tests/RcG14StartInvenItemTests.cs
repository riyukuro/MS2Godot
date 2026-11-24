using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G14 — startInvenItem (D24): GMS2 table/job.xml job code 010 lists &lt;startInvenItem&gt; 13200220 and 14100190, job 001
/// 13000001; KMS job 10 has startItems="13200220,14100190". WeaponStance.StartItemIds reads either shape.
/// </summary>
public class RcG14StartInvenItemTests
{
    [Test]
    public void TheKmsJobKeepsItsStartItems()
    {
        using var packs = new PackSet(RcGap.KmsData());
        Assert.That(WeaponStance.StartItemIds(packs, 10), Is.EqualTo(new[] { 13200220, 14100190 }));
    }

    [Test]
    public void TheGms2JobReadsItsStartInvenItems()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        Assert.That(WeaponStance.StartItemIds(packs, 10), Is.EqualTo(new[] { 13200220, 14100190 }), "job code 010 startInvenItem");
        Assert.That(WeaponStance.StartItemIds(packs, 1), Is.EqualTo(new[] { 13000001 }), "job code 001 has no KMS startItems row");
    }
}
