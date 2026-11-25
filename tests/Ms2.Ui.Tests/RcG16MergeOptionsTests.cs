using System.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G16 — merge options (D15): GMS2 table/itemmergeoptionbase.xml wraps the slots in &lt;mergeStep&gt; and writes idxN="min,max"
/// (option 37000001 part 13 pap idx0="3,4" .. idx9="18,19", so the first range is 3..19). KMS option 37000013 part 13 has
/// min="2" idx0_max="4" .. idx9_max="19", the same 3..19.
/// </summary>
public class RcG16MergeOptionsTests
{
    static ItemData Equip(int subtype) => new() { Type = 1, Subtype = subtype };

    static ItemData Material(int optionId) => new() { Type = 0x13, Subtype = 1, Param1 = optionId };

    static (int Min, int Max) PapRange(GameTables g, IReadOnlyList<ItemMergeOptionTable.Option> options)
    {
        Assert.That(options, Is.Not.Null);
        int pap = g.ItemOptions.FindStatByName("pap");
        var o = options.FirstOrDefault(x => x.Stat == pap);
        Assert.That(o, Is.Not.Null, "the pap option of slot part 13");
        return (o.FirstMin, o.FirstMax);
    }

    [Test]
    public void TheKmsMergeOptionKeepsItsFirstRange()
    {
        using var packs = new PackSet(RcGap.KmsData());
        var g = new GameTables(packs, "en");
        var options = g.MergeOptions.Find(Equip(13), Material(37000013));
        Assert.That(PapRange(g, options), Is.EqualTo((3, 19)), "kms 37000013 min=2 idx0_max=4 .. idx9_max=19");
    }

    [Test]
    public void TheGms2MergeOptionReadsItsSteps()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var g = new GameTables(packs, "en");
        var options = g.MergeOptions.Find(Equip(13), Material(37000001));
        Assert.That(PapRange(g, options), Is.EqualTo((3, 19)), "gms 37000001 pap idx0=\"3,4\" .. idx9=\"18,19\"");
    }
}
