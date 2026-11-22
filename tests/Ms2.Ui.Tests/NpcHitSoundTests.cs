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
    public void TheRelationIsTheOwnersObjectKind()
    {
        Assert.That(NpcHitSound.Relation(HitOwner.None, false), Is.EqualTo(0));
        Assert.That(NpcHitSound.Relation(HitOwner.Pc, true), Is.EqualTo(1));
        Assert.That(NpcHitSound.Relation(HitOwner.Pc, false), Is.EqualTo(3));
        Assert.That(NpcHitSound.Relation(HitOwner.Npc, false), Is.EqualTo(3));
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
