using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// G06 — quest complete@fieldID (D5): GMS2 quest/10002800.xml has &lt;complete npc="11001294" fieldID="52010004"&gt;;
/// QuestTable reads complete@npc only. KMS questdata.xml quest 10002800 has the same npc and no fieldID.
/// </summary>
public class RcG06QuestCompleteFieldTests
{
    [Test]
    public void TheKmsQuestKeepsItsCompleteNpc()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var q = QuestTable.Load(packs, new[] { "en" }, "KR", _ => true).Quest(10002800);
        Assert.That(q, Is.Not.Null, "kms questdata.xml quest 10002800");
        Assert.That(q.CompleteNpc, Is.EqualTo(11001294));
    }

    [Test]
    [Ignore("G06: QuestData has no CompleteField and QuestTable reads complete@npc only")]
    public void TheGms2QuestReadsItsCompleteField()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var q = QuestTable.Load(packs, new[] { "en" }, "NA", _ => true).Quest(10002800);
        Assert.That(q, Is.Not.Null, "gms/quest/10002800.xml");
        Assert.That(q.CompleteNpc, Is.EqualTo(11001294));
        Assert.That(RcGap.Member<int>(q, "CompleteField"), Is.EqualTo(52010004), "<complete fieldID=\"52010004\">");
    }
}
