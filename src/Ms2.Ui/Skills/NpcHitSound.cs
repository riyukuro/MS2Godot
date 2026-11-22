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
    /// The owner's object kind (WorldService::FUN_141bd19d0, worldservice_1.cpp:6296, GetObjectKind :6304): 0 when the id
    /// finds nothing, 1 for the own PC (MyPC::vfunc_58, mypc.cpp:1247), 3 for another PC or an npc (PCBase::vfunc_58,
    /// pcbase.cpp:1092). STILL-OPEN: kind 2 (no PC or npc returns it) is not produced here.
    /// </summary>
    public static int Relation(HitOwner owner, bool byLocalPlayer) =>
        owner == HitOwner.None ? 0 : byLocalPlayer ? 1 : 3;

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
