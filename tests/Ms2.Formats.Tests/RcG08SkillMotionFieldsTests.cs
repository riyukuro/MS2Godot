using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G08 — skill motion fields (C5, D1): GMS2 skill/00/00000001.xml has motionProperty holdWeapon="1", faceTarget="1",
/// weaponSequenceName="" and strTagEffects="e0=CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml" (lifted onto &lt;motion&gt; by
/// ToSkillRows); SkillMotion reads the four (strTagEffects as TagEffects). KMS skilldata/000.xml skill id 1 has the same row.
/// </summary>
public class RcG08SkillMotionFieldsTests
{
    [Test]
    public void TheKmsMotionKeepsItsSequenceAndAttackPoint()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var m = new SkillTable(packs).Motion(1, 1, 0);
        Assert.That(m, Is.Not.Null, "kms/skilldata/000.xml skill 1 level 1");
        Assert.That((m.SequenceName, m.AttackPoints), Is.EqualTo(("Attack_01_A", new List<string> { "p0" })));
    }

    [Test]
    public void TheGms2MotionReadsItsFourFields()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var m = new SkillTable(packs).Motion(1, 1, 0);
        Assert.That(m, Is.Not.Null, "gms/skill/00/00000001.xml skill 1 level 1");
        Assert.That(RcGap.Member<int>(m, "HoldWeapon"), Is.EqualTo(1));
        Assert.That(RcGap.Member<int>(m, "FaceTarget"), Is.EqualTo(1));
        Assert.That(RcGap.Member<string>(m, "WeaponSequenceName"), Is.EqualTo(""));
        // strTagEffects is the client's m_mapTagEffects (key -> effect), SkillMotion.TagEffects
        Assert.That(m.TagEffects, Is.EqualTo(new Dictionary<string, string> { ["e0"] = "CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml" }));
    }

    [Test]
    public void TheMotionKeepsItsHoldWeaponAndWeaponSequenceOnEitherData()
    {
        // KMS skilldata/100.xml and GMS2 skill/10/10000021.xml (Snail Throw): holdWeapon="0"; skilldata/106.xml and
        // skill/10/10600001.xml (Gatling Fire): weaponSequenceName="Attack_01_A"; neither has faceTarget, so it stays 1
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new SkillTable(packs);
        var snail = t.Motion(10000021, 1, 0);
        Assert.That((snail.HoldWeapon, snail.FaceTarget, snail.WeaponSequenceName), Is.EqualTo((0, 1, "")));
        var gatling = t.Motion(10600001, 1, 0);
        Assert.That((gatling.HoldWeapon, gatling.FaceTarget, gatling.WeaponSequenceName), Is.EqualTo((1, 1, "Attack_01_A")));
    }
}
