using Ms2.Formats.World;

namespace Ms2.Ui.Skills;

/// <summary>Who an event voice of <see cref="CombatVoice"/> is played for: nobody, the hit PC, the damage record's owner.</summary>
public enum VoiceActor { None, Target, Owner }

/// <summary>
/// The combat sounds of a damage record that are not the npc's own hit sound (<see cref="NpcHitSound"/>): the PC's hit sound
/// (PC::vfunc_33 @0x141652080 pc.cpp:955-957, vfunc_35 :1078-1080), the PC's death voices (PC::FUN_141653740 @0x141653740,
/// pc.cpp:1173) and a boss's kill voices (CNpc::vfunc_33, unit_14108e1e0_npcactorandvibrate.cpp:1889-1902).
/// A mode-1 record's owner is the packet's caster and its m_nCasterId the target's id (FUN_14171cdf0,
/// scenestategame.cpp:5611-5624), so "caster kind" here is the hit PC's kind.
/// The event types are FUN_141793500's (Ms2.Audio.EventSoundTable).
/// </summary>
public static class CombatVoice
{
    public const int BossKill = 1, BossEndKill = 2, OtherPcKill = 3, MyPcDead = 5;

    /// <summary>
    /// The object kind of what an id finds (WorldService::FUN_141bd19d0, worldservice_1.cpp:6296, GetObjectKind :6304):
    /// 0 for nothing, 1 for the own PC (MyPC::vfunc_58, mypc.cpp:1247), 3 for another PC (PCBase::vfunc_58,
    /// pcbase.cpp:1092), 5 for an npc and 6 for a boss npc (CNpc::vfunc_58, unit_14108e1e0_npcactorandvibrate.cpp:655).
    /// </summary>
    public static int ObjectKind(HitOwner owner, bool local, bool boss) => owner switch
    {
        HitOwner.Pc => local ? 1 : 3,
        HitOwner.Npc => boss ? 6 : 5,
        _ => 0,
    };

    /// <summary>
    /// A PC's cast or splash hit plays its sound when the hit pushes it or takes health (total below 0) (pc.cpp:946-957):
    /// PlayHitSound(GetAttackMaterial(), defence material 1, the owner's kind, the PC's place, this volume):
    /// VolumeNpcToMyPc (1 by default, constantsblocks.cpp:792) when the hit PC's kind is 1 or 2, else VolumeNpcToPc (0.5, :793).
    /// </summary>
    public static float PcHitVolume(int targetKind, ClientConstants constants) =>
        targetKind is 1 or 2 ? Float(constants, "VolumeNpcToMyPc", 1f) : Float(constants, "VolumeNpcToPc", 0.5f);

    /// <summary>The PC's defence material in its hit sound: the constant 1 (pc.cpp:957, :1080).</summary>
    public const int PcDefenceMaterial = 1;

    /// <summary>
    /// The attack material and volume of the hit sound a record of <paramref name="mode"/> plays on a PC (against
    /// <see cref="PcDefenceMaterial"/>), null for none: a cast's (PC::vfunc_33, mode 1) or a splash's (vfunc_35, modes 5 and 6)
    /// when its total is below 0, with the record's attack material (<see cref="NpcHitSound.AttackMaterial"/>; -1, not known
    /// here, plays none); a dot's (vfunc_34, pc.cpp:990-998, mode 3) whatever its total, with its buff level's
    /// &lt;DotDamage soundMaterial&gt; (0 plays none). The volume is <see cref="PcHitVolume"/> of the hit PC's kind.
    /// STILL-OPEN: the cast's push in the grab state (bPush, state 0x2b) with no health taken plays none here.
    /// </summary>
    public static (int Attack, float Volume)? PcHitSound(int mode, long total, int targetKind, int attackMaterial, int dotMaterial,
        ClientConstants constants)
    {
        if (mode == 3) return dotMaterial == 0 ? null : (dotMaterial, PcHitVolume(targetKind, constants));
        return total < 0 && attackMaterial >= 0 ? (attackMaterial, PcHitVolume(targetKind, constants)) : null;
    }

    /// <summary>
    /// The attack material and volume of a dot's hit sound on an npc (CNpc::vfunc_34,
    /// unit_14108e1e0_npcactorandvibrate.cpp:1927-1935, against the npc's defenceMaterial), whatever its total: its buff
    /// level's &lt;DotDamage soundMaterial&gt;, at <see cref="NpcHitSound.Volume"/> of the owner's kind; null for material 0.
    /// </summary>
    public static (int Attack, float Volume)? NpcDotSound(int ownerKind, int dotMaterial, ClientConstants constants) =>
        dotMaterial == 0 ? null : (dotMaterial, NpcHitSound.Volume(ownerKind, constants));

    /// <summary>
    /// PC::FUN_141653740(ownerId, casterId, owner kind, caster kind), called after a record on a PC that is dead (IsDead,
    /// PCBase m_unk_15c, which only the dead user / user state packets set: PC::vfunc_137, pc.cpp:4593): a boss's record on the
    /// own PC plays myPC_Dead for the PC; a PC's (own or other) record plays otherPC_Kill for the owner; an npc's plays nothing.
    /// The rows are onlyCasterMyPC, so the second plays only when the owner is the own PC (PlayEventVoice, effectservice.cpp:1371).
    /// </summary>
    public static (int Type, VoiceActor Actor) PcDeathVoice(int ownerKind, int targetKind)
    {
        if (ownerKind == 6) return targetKind == 1 ? (MyPcDead, VoiceActor.Target) : (0, VoiceActor.None);
        return ownerKind is 1 or 3 ? (OtherPcKill, VoiceActor.Owner) : (0, VoiceActor.None);
    }

    /// <summary>
    /// The voice of a boss's death by a record (CNpc::vfunc_33 :1889-1902, played for the own PC): Boss_EndKill when the own PC
    /// owns the record; else Boss_Kill when the own PC is alive, the boss's focus distance (GameObject m_unk_bc,
    /// GameObject::FUN_14123daa0) is within BossKillSoundRange (1500 by default, constantsblocks.cpp:1496) and the own PC hit
    /// it before (MyPC::FUN_14152e280, m_set15f8, filled by <see cref="BossHits"/>). 0 for a non-boss.
    /// </summary>
    public static int BossDeathVoice(bool boss, int ownerKind, bool playerAlive, float distance, float range, bool hitByPlayer)
    {
        if (!boss) return 0;
        if (ownerKind == 1) return BossEndKill;
        return playerAlive && distance <= range && hitByPlayer ? BossKill : 0;
    }

    /// <summary>BossKillSoundRange (constantsblocks.cpp:1496).</summary>
    public static float BossKillRange(ClientConstants constants) => Float(constants, "BossKillSoundRange", 1500f);

    static float Float(ClientConstants c, string key, float def) => c?.Float(key, def) ?? def;
}

/// <summary>
/// MyPC m_set15f8 (mypc.h:302): the bosses the own PC's cast hit took health from (MyPC::FUN_14152e240, inserted at
/// CNpc::vfunc_33 :1871), which <see cref="CombatVoice.BossDeathVoice"/> asks (FUN_14152e280). Never cleared by the client;
/// held per field here, as the own PC is.
/// </summary>
public sealed class BossHits
{
    readonly HashSet<int> ids = new();
    readonly HashSet<int> killed = new();
    public void Add(int id) => ids.Add(id);
    public bool Contains(int id) => ids.Contains(id);

    /// <summary>
    /// True for the first record that finds the boss's hp at 0, the client's killing record (the !IsDead branch of
    /// CNpc::vfunc_33 :1889). Here an hp 0 stat packet can kill the npc before that record runs (NpcLayer.SetHp), so the
    /// voice asks this instead of the dead flag; a later record on the same boss plays none.
    /// </summary>
    public bool FirstKill(int id) => killed.Add(id);

    public void Clear() { ids.Clear(); killed.Clear(); }
}
