using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// G08 — skill motion fields (C5, D1): GMS2 skill/00/00000001.xml has motionProperty holdWeapon="1", faceTarget="1",
/// weaponSequenceName="" and strTagEffects="e0=CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml" (lifted onto &lt;motion&gt; by
/// ToSkillRows); SkillMotion/&lt;motion&gt; reads none of the four. KMS skilldata/000.xml skill id 1 has the same row.
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
    [Ignore("G08: SkillMotion does not read holdWeapon/faceTarget/weaponSequenceName/strTagEffects yet")]
    public void TheGms2MotionReadsItsFourFields()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var m = new SkillTable(packs).Motion(1, 1, 0);
        Assert.That(m, Is.Not.Null, "gms/skill/00/00000001.xml skill 1 level 1");
        Assert.That(RcGap.Member<int>(m, "HoldWeapon"), Is.EqualTo(1));
        Assert.That(RcGap.Member<int>(m, "FaceTarget"), Is.EqualTo(1));
        Assert.That(RcGap.Member<string>(m, "WeaponSequenceName"), Is.EqualTo(""));
        var tags = RcGap.Member<List<(string Key, string Path)>>(m, "StrTagEffects");
        Assert.That(tags, Is.EqualTo(new List<(string, string)> { ("e0", "CH/Weapon/Hand/Eff_Hand_Attack01_Cast_01.xml") }));
    }
}
