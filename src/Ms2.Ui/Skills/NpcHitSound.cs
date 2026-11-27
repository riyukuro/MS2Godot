using Ms2.Formats.World;

namespace Ms2.Ui.Skills;

/// <summary>What a damage record's owner id finds in the field (WorldService::FUN_141bd1200): nothing, an npc or a PC.</summary>
public enum HitOwner { None, Npc, Pc }

/// <summary>
/// The inputs of the hit sound an npc plays for a damage record: CEffectService::PlayHitSound(GetAttackMaterial(),
/// m_nDefenceMaterial, relation, position, volume) in CNpc::vfunc_33 (unit_14108e1e0_npcactorandvibrate.cpp:1875-1880)
/// and vfunc_35 (:1979-1982).
/// </summary>
public static class NpcHitSound
{
    /// <summary>
    /// The owner's object kind (WorldService::FUN_141bd19d0, worldservice_1.cpp:6296, GetObjectKind :6304;
    /// <see cref="CombatVoice.ObjectKind"/>): 0 when the id finds nothing, 1 for the own PC, 3 for another PC, 5 for an npc
    /// and 6 for a boss npc (CNpc::vfunc_58, unit_14108e1e0_npcactorandvibrate.cpp:655).
    /// STILL-OPEN: kind 2 (no PC or npc returns it) is not produced here.
    /// </summary>
    public static int Relation(HitOwner owner, bool byLocalPlayer, bool ownerBoss = false) =>
        CombatVoice.ObjectKind(owner, byLocalPlayer, ownerBoss);

    /// <summary>
    /// CDamageRecord::GetAttackMaterial (@0x14175ad50, skillrecord.cpp:580): 1 when the owner is not in the field; an npc's
    /// is its attack's attackMaterial; a PC's is its attack's when not 0, else the material of its weapon item.
    /// -1 when it cannot be told here: no attack row, or a PC's attack of material 0.
    /// STILL-OPEN: the PC's weapon (CEquipment::FUN_1411e1860(5, 6, 0), the item data's m_nAttackMaterial) is not ported.
    /// </summary>
    public static int AttackMaterial(HitOwner owner, SkillAttackDamage attack)
    {
        if (owner == HitOwner.None) return 1;
        if (attack == null) return -1;
        int material = attack.AttackMaterial;
        if (owner == HitOwner.Npc || material != 0) return material;
        return -1;
    }

    /// <summary>
    /// The attack a damage record's GetAttackData() reads for its material: a cast's (mode 1) by the packet's skill, level,
    /// motion and attack; a region skill's (mode 5) by its region object's skill and level (CRegionSkillObject m_spSkill,
    /// <paramref name="region"/>), motion 0 and the packet's attack (OnRegionSkillDamage, scenestategame.cpp:7783-7785),
    /// none when the region is unknown; none for the rest: a dot's sound is not ported, and a splash's (mode 6) record has
    /// owner 0 (OnSplashDamage, :5743), so its material is 1 without the attack.
    /// </summary>
    public static SkillAttackDamage RecordAttack(Ms2.Net.Packets.SkillDamage d, System.Func<int, (int Skill, short Level)?> region,
        System.Func<int, int, int, int, SkillAttackDamage> attackOf)
    {
        switch (d.Mode)
        {
            case 1: return attackOf(d.SkillId, d.Level, d.Motion, d.AttackPoint);
            case 5: return region?.Invoke(d.RegionId) is { } r ? attackOf(r.Skill, r.Level, 0, d.AttackPoint) : null;
            default: return null;
        }
    }

    /// <summary>
    /// The volume: VolumeMyPcToNpc (1 by default, constantsblocks.cpp:787) for relation 1 or 2, else VolumePcToNpc (0.5,
    /// :790). VolumeOtherPc for relation 3 is applied later by CEffectService::FUN_1411a5110 (effectservice.cpp:1336), in
    /// FieldAudio.PlayHitSound.
    /// </summary>
    public static float Volume(int relation, ClientConstants constants)
    {
        return relation is 1 or 2 ? Float(constants, "VolumeMyPcToNpc", 1f) : Float(constants, "VolumePcToNpc", 0.5f);
    }

    static float Float(ClientConstants c, string key, float def) => c?.Float(key, def) ?? def;
}
