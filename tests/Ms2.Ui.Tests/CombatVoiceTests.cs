using Ms2.Audio;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The combat sounds of a damage record besides the npc's own hit sound: the object kinds, the PC's hit sound volume, the PC's
/// death voices (PC::FUN_141653740) and a boss's kill voices (CNpc::vfunc_33's death branch).
/// </summary>
public class CombatVoiceTests
{
    [Test]
    public void TheEventTypesAreTheTablesOnes()
    {
        Assert.That((CombatVoice.BossKill, CombatVoice.BossEndKill, CombatVoice.OtherPcKill, CombatVoice.MyPcDead),
            Is.EqualTo((EventSoundTable.BossKill, EventSoundTable.BossEndKill, EventSoundTable.OtherPcKill, EventSoundTable.MyPcDead)));
    }

    [Test]
    public void TheObjectKindTellsAnNpcAndABossFromAPc()
    {
        Assert.That(CombatVoice.ObjectKind(HitOwner.None, false, false), Is.EqualTo(0));
        Assert.That(CombatVoice.ObjectKind(HitOwner.Pc, true, false), Is.EqualTo(1), "MyPC::vfunc_58");
        Assert.That(CombatVoice.ObjectKind(HitOwner.Pc, false, false), Is.EqualTo(3), "PCBase::vfunc_58");
        Assert.That(CombatVoice.ObjectKind(HitOwner.Npc, false, false), Is.EqualTo(5), "CNpc::vfunc_58");
        Assert.That(CombatVoice.ObjectKind(HitOwner.Npc, false, true), Is.EqualTo(6), "CNpc::vfunc_58 for a boss");
    }

    [Test]
    public void TheHitPcsKindPicksItsHitSoundsVolume()
    {
        Assert.That(CombatVoice.PcHitVolume(1, null), Is.EqualTo(1f), "the own PC: VolumeNpcToMyPc");
        Assert.That(CombatVoice.PcHitVolume(2, null), Is.EqualTo(1f));
        Assert.That(CombatVoice.PcHitVolume(3, null), Is.EqualTo(0.5f), "another PC: VolumeNpcToPc");
        Assert.That(CombatVoice.PcDefenceMaterial, Is.EqualTo(1));
    }

    [Test]
    public void APcsHitSoundByRecordMode()
    {
        Assert.That(CombatVoice.PcHitSound(1, -10, 1, 20, 0, null), Is.EqualTo(((int, float)?)(20, 1f)), "a cast taking health from the own PC");
        Assert.That(CombatVoice.PcHitSound(5, -10, 3, 20, 0, null), Is.EqualTo(((int, float)?)(20, 0.5f)), "a splash on another PC: VolumeNpcToPc");
        Assert.That(CombatVoice.PcHitSound(1, 0, 1, 20, 0, null), Is.Null, "a cast that takes no health");
        Assert.That(CombatVoice.PcHitSound(1, 10, 1, 20, 0, null), Is.Null, "a heal");
        Assert.That(CombatVoice.PcHitSound(1, -10, 1, -1, 0, null), Is.Null, "an attack material not known");
        Assert.That(CombatVoice.PcHitSound(3, 0, 1, -1, 104, null), Is.EqualTo(((int, float)?)(104, 1f)),
            "a dot plays its buff level's soundMaterial whatever its total");
        Assert.That(CombatVoice.PcHitSound(3, -10, 1, 20, 0, null), Is.Null, "a dot whose level has no soundMaterial");
    }

    [Test]
    public void AnNpcsDotSound()
    {
        Assert.That(CombatVoice.NpcDotSound(1, 100, null), Is.EqualTo(((int, float)?)(100, 1f)), "the own PC's dot: VolumeMyPcToNpc");
        Assert.That(CombatVoice.NpcDotSound(3, 100, null), Is.EqualTo(((int, float)?)(100, 0.5f)), "another PC's: VolumePcToNpc");
        Assert.That(CombatVoice.NpcDotSound(1, 0, null), Is.Null, "no soundMaterial");
    }

    [TestCase(6, 1, CombatVoice.MyPcDead, VoiceActor.Target, TestName = "a boss's record on the dead own PC: myPC_Dead for it")]
    [TestCase(6, 3, 0, VoiceActor.None, TestName = "a boss's record on another dead PC: nothing")]
    [TestCase(5, 1, 0, VoiceActor.None, TestName = "a plain npc's record on the dead own PC: nothing")]
    [TestCase(1, 3, CombatVoice.OtherPcKill, VoiceActor.Owner, TestName = "the own PC's record on a dead PC: otherPC_Kill for the own PC")]
    [TestCase(3, 1, CombatVoice.OtherPcKill, VoiceActor.Owner, TestName = "another PC's record on the dead own PC: otherPC_Kill for that PC")]
    [TestCase(0, 1, 0, VoiceActor.None, TestName = "no owner: nothing")]
    public void ThePcsDeathVoice(int ownerKind, int targetKind, int type, VoiceActor actor) =>
        Assert.That(CombatVoice.PcDeathVoice(ownerKind, targetKind), Is.EqualTo((type, actor)));

    [Test]
    public void ABossesDeathVoice()
    {
        const float range = 1500f;
        Assert.That(CombatVoice.BossDeathVoice(false, 1, true, 0f, range, true), Is.EqualTo(0), "not a boss");
        Assert.That(CombatVoice.BossDeathVoice(true, 1, false, 99999f, range, false), Is.EqualTo(CombatVoice.BossEndKill),
            "the own PC's record: Boss_EndKill, whatever the rest");
        Assert.That(CombatVoice.BossDeathVoice(true, 3, true, range, range, true), Is.EqualTo(CombatVoice.BossKill),
            "another's record, the own PC alive, at the range, hit before: Boss_Kill");
        Assert.That(CombatVoice.BossDeathVoice(true, 5, true, 10f, range, true), Is.EqualTo(CombatVoice.BossKill), "an npc's record too");
        Assert.That(CombatVoice.BossDeathVoice(true, 3, true, range + 1, range, true), Is.EqualTo(0), "beyond the range");
        Assert.That(CombatVoice.BossDeathVoice(true, 3, false, 10f, range, true), Is.EqualTo(0), "the own PC dead");
        Assert.That(CombatVoice.BossDeathVoice(true, 3, true, 10f, range, false), Is.EqualTo(0), "never hit by the own PC");
        Assert.That(CombatVoice.BossKillRange(null), Is.EqualTo(1500f));
    }

    [Test]
    public void TheBossHitSetHoldsTheBossesHit()
    {
        var hits = new BossHits();
        Assert.That(hits.Contains(7), Is.False);
        hits.Add(7);
        hits.Add(7);
        Assert.That((hits.Contains(7), hits.Contains(8)), Is.EqualTo((true, false)));
        hits.Clear();
        Assert.That(hits.Contains(7), Is.False);
    }

    /// <summary>
    /// The killing record voices once: an hp 0 stat packet (/kill sends it before its record) kills the npc first, so the
    /// record finds it dead, and the voice still plays for the first record at hp 0 and never for a later one.
    /// </summary>
    [Test]
    public void ABossDeathVoicesForItsFirstRecordAtHpZeroOnly()
    {
        var hits = new BossHits();
        Assert.That((hits.FirstKill(7), hits.FirstKill(7), hits.FirstKill(8)), Is.EqualTo((true, false, true)));
        hits.Clear();
        Assert.That(hits.FirstKill(7), Is.True, "a new field");
    }
}
