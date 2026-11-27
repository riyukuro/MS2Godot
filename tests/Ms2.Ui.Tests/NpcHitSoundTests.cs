using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The inputs of an npc's hit sound: CDamageRecord::GetAttackMaterial, the owner's object kind and the volume.</summary>
public class NpcHitSoundTests
{
    static SkillAttackDamage Attack(int material) => new() { AttackMaterial = material };

    [Test]
    public void TheAttackMaterialFollowsTheOwner()
    {
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.None, null), Is.EqualTo(1), "no owner: 1, the attack unread");
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.None, Attack(7)), Is.EqualTo(1));
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.Npc, Attack(0)), Is.EqualTo(0), "an npc's is its attack's, 0 too");
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.Npc, Attack(7)), Is.EqualTo(7));
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.Pc, Attack(7)), Is.EqualTo(7));
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.Pc, Attack(0)), Is.EqualTo(-1), "a PC's 0 goes to its weapon");
        Assert.That(NpcHitSound.AttackMaterial(HitOwner.Npc, null), Is.EqualTo(-1));
    }

    [Test]
    public void ARegionSkillsRecordTakesItsRegionsAttack()
    {
        (int, int, int, int) asked = default;
        SkillAttackDamage AttackOf(int s, int l, int m, int a) { asked = (s, l, m, a); return Attack(9); }
        (int Skill, short Level)? Region(int id) => id == 40 ? (10500001, 3) : null;
        var region = new Ms2.Net.Packets.SkillDamage { Mode = 5, OwnerId = 7, RegionId = 40, AttackPoint = 2 };
        Assert.That(NpcHitSound.RecordAttack(region, Region, AttackOf)?.AttackMaterial, Is.EqualTo(9), "mode 5: the region's attack");
        Assert.That(asked, Is.EqualTo((10500001, 3, 0, 2)), "the region's skill and level, motion 0, the packet's attack");
        region.RegionId = 41;
        Assert.That(NpcHitSound.RecordAttack(region, Region, AttackOf), Is.Null, "an unknown region: none");
        var cast = new Ms2.Net.Packets.SkillDamage { Mode = 1, SkillId = 10100161, Level = 2, Motion = 1, AttackPoint = 3 };
        Assert.That(NpcHitSound.RecordAttack(cast, Region, AttackOf), Is.Not.Null);
        Assert.That(asked, Is.EqualTo((10100161, 2, 1, 3)), "mode 1: the packet's skill, level, motion and attack");
        Assert.That(NpcHitSound.RecordAttack(new Ms2.Net.Packets.SkillDamage { Mode = 6, SkillId = 1 }, Region, AttackOf), Is.Null, "mode 6: owner 0");
    }

    [Test]
    public void TheRelationIsTheOwnersObjectKind()
    {
        Assert.That(NpcHitSound.Relation(HitOwner.None, false), Is.EqualTo(0));
        Assert.That(NpcHitSound.Relation(HitOwner.Pc, true), Is.EqualTo(1));
        Assert.That(NpcHitSound.Relation(HitOwner.Pc, false), Is.EqualTo(3));
        Assert.That(NpcHitSound.Relation(HitOwner.Npc, false), Is.EqualTo(5), "CNpc::vfunc_58: an npc is 5, not another PC's 3");
        Assert.That(NpcHitSound.Relation(HitOwner.Npc, false, ownerBoss: true), Is.EqualTo(6));
    }

    [Test]
    public void TheVolumeTakesTheConstantsOfTheRelation()
    {
        Assert.That(NpcHitSound.Volume(1, null), Is.EqualTo(1f));
        Assert.That(NpcHitSound.Volume(0, null), Is.EqualTo(0.5f));
        Assert.That(NpcHitSound.Volume(3, null), Is.EqualTo(0.5f), "another's: VolumePcToNpc, VolumeOtherPc comes later");
        var c = new ClientConstants(new Dictionary<string, string> { ["VolumeMyPcToNpc"] = "0.8", ["VolumePcToNpc"] = "0.25", ["VolumeOtherPc"] = "0.5" });
        Assert.That(NpcHitSound.Volume(2, c), Is.EqualTo(0.8f));
        Assert.That(NpcHitSound.Volume(0, c), Is.EqualTo(0.25f));
        Assert.That(NpcHitSound.Volume(3, c), Is.EqualTo(0.25f));
    }
}
