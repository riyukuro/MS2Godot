using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G12 — map properties C16: gms/map/00000001.xml has property enterMinLevel="0" enterMaxLevel="0"
/// enterAdminLevel="0" limitContent="0" exploreType="0" and cashCall cashTaxiNotDestination="1"; FieldData reads none of
/// them. KMS table/fielddata.xml id 1 keeps its MapType/Capacity (0/0, no property attributes).
/// </summary>
public class RcG12MapPropertiesTests
{
    [Test]
    public void TheKmsFieldKeepsItsMapTypeAndCapacity()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null);
        Assert.That((f.MapType, f.Capacity), Is.EqualTo((0, 0)));
    }

    [Test]
    [Ignore("G12: FieldData does not read enterMinLevel/enterMaxLevel/enterAdminLevel/limitContent/exploreType/recoveryFullHP/dropRank/globalDropBoxID/cashCall yet")]
    public void TheGms2LoginMapReadsTheC16Properties()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var f = FieldData.FindById(packs, 1);
        Assert.That(f, Is.Not.Null, "gms/map/00000001.xml");
        Assert.That(RcGap.Member<int>(f, "EnterMinLevel"), Is.EqualTo(0));
        Assert.That(RcGap.Member<int>(f, "EnterMaxLevel"), Is.EqualTo(0));
        Assert.That(RcGap.Member<int>(f, "EnterAdminLevel"), Is.EqualTo(0));
        Assert.That(RcGap.Member<uint>(f, "LimitContent"), Is.EqualTo(0u));
        Assert.That(RcGap.Member<int>(f, "ExploreType"), Is.EqualTo(0));
        Assert.That(RcGap.Member<int>(f, "DropRank"), Is.EqualTo(0));
        Assert.That(RcGap.Member<int>(f, "GlobalDropBoxId"), Is.EqualTo(0));
        Assert.That(RcGap.Member<bool>(f, "CashTaxiNotDeparture"), Is.False);
        Assert.That(RcGap.Member<bool>(f, "CashTaxiNotDestination"), Is.True);
        Assert.That(RcGap.Member<bool>(f, "CashCallMedicProhibit"), Is.False);
        Assert.That(RcGap.Member<bool>(f, "CashCallMarketProhibit"), Is.False);
        Assert.That(typeof(FieldData).GetMethod("HasLimitContent", new[] { typeof(uint) }), Is.Not.Null, "CFieldDataCli::HasLimitContent");
    }
}
