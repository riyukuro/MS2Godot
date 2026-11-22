using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The attach type CNifEffect::vfunc_3 switches on (nifeffectparam.cpp:667): ParseAttachType (:275) and attachToNode (:398).
/// </summary>
public class EffectAttachKindTests
{
    [Test]
    public void AttachTypeNamesMapAsTheClientParsesThem()
    {
        Assert.That(new[] { "Node", "mesh", "WEAPONL", "WeaponR", "", null, "Scene" }.Select(EffectPart.AttachTypeOf),
            Is.EqualTo(new[] { 1, 2, 3, 4, 0, 0, 0 }));
        Assert.That(new EffectPart { AttachType = "Mesh", AttachToNode = true }.AttachKind, Is.EqualTo(1), "attachToNode sets 1");
        Assert.That(new EffectPart { AttachType = "Mesh" }.AttachKind, Is.EqualTo(2));
        var p = new EffectPart();
        Assert.That((p.AttachKind, p.ApplyTranslate, p.ApplyRotation, p.ApplyScale), Is.EqualTo((0, true, true, true)),
            "no attachInfo: type 0, every bit applied");
    }

    [Test]
    public void TheStaffAttackRemainIsAWorldPartAndTheBigBurnKeepANodePart()
    {
        var (packs, _) = NifEnv.Open();
        var t = new EffectTable(packs);
        var staff = t.Get("CH/Weapon/Staff/Eff_Staff_Attack01_Remain_01.xml")?.Where(p => p.Type == "nif").ToList();
        Assert.That(staff, Is.Not.Null.And.Not.Empty);
        Assert.That(staff.Select(p => p.AttachKind), Is.All.EqualTo(0), "type 0: the world effect root");
        Assert.That(staff.All(p => p.ApplyNodeTransform), Is.True);
        var burn = t.Get("additional/burn/eff_additional_bigburn_keep.xml")?.Where(p => p.Type == "nif").ToList();
        Assert.That(burn, Is.Not.Null.And.Not.Empty);
        Assert.That(burn.Select(p => p.AttachKind), Is.All.EqualTo(1), "attachToNode: under the actor");
    }
}
