using System.Collections.Generic;
using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>One number a damage record asks UIDamageText for: PCBase::vfunc_111's arguments (the target, the absolute value,
/// the hit's type as the grade, the record's owner as the caster).</summary>
public readonly record struct DamageNumber(int TargetId, long Value, int Grade, int CasterId);

/// <summary>
/// The damage numbers of an inbound skill damage packet and UIDamageText's show test.
/// </summary>
public static class DamageNumbers
{
    /// <summary>UIDamageText::FUN_141914530 (@0x141914530): grades 5, 6 and 7 are recover numbers.</summary>
    public static bool IsRecover(int grade) => grade is 5 or 6 or 7;

    /// <summary>
    /// The numbers of a mode 1 packet (CSceneStateGame::FUN_14171cdf0 @0x14171cdf0, scenestategame.cpp:5606), in the client's
    /// order: per target a CDamageRecord whose owner is the packet's owner (Init, skillrecord.cpp:466, m_nOwnerId +0x2c);
    /// CDamageRecord::AddDamage (@0x14175abc0, skillrecord.cpp:554) keeps every hit and turns type 8 into 3; then
    /// PCBase::FUN_141671c20 (@0x141671c20, pcbase.cpp:3922) gives one vfunc_111 per hit with abs(damage)
    /// (pcbase.cpp:2844). The record's hit point (+0x78) stays zero (the number goes to the target's anchor) unless the
    /// caster is an npc with property tag 0x11.
    /// Nothing unless the packet's attack has damage (<see cref="HasDamage"/>).
    /// STILL-OPEN: main type 0x12's miss effect when it has none (Constants m_8350 per type 2 hit, pcbase.cpp:3942), the
    /// sight-range test, the tag 0x11 hit point, and the magic service's delivery of the hit (FUN_14146b440, only with a
    /// caster object; the numbers are made when the packet comes); modes 0, 5 and 6 are not listed here (mode 3 is the dot number).
    /// <para>Mode 4 (CSceneStateGame::FUN_14171d820 @0x14171d820, scenestategame.cpp:5685-5726): nothing when the owner is
    /// no object of the field (FUN_141bd1200, <paramref name="isObject"/>; a null test means none is); with the show byte,
    /// each nonzero value of the three in order is one number of type 5, 6 and 7 (FUN_1417403b0 @0x1417403b0, :9005) on the
    /// owner, with the packet's target as the caster (pOwner->vfunc_111(value, type, nTargetId, ZERO)). STILL-OPEN: the
    /// buff's invoke effect on the owner (FUN_14096fc00, additionalcli_2.cpp:325), the DPS meter's AddStat / AddHealTarget
    /// and CNpc::vfunc_111's skip of a dead npc whose data has CorpseHitAble (@0x141090620).</para>
    /// </summary>
    public static void Records(SkillDamage d, List<DamageNumber> into, System.Func<int, bool> isObject, SkillDamageTable damage)
    {
        if (d.Mode == 4)
        {
            if (isObject == null || !isObject(d.OwnerId) || !d.ShowValues) return;
            if (d.Value1 != 0) into.Add(new DamageNumber(d.OwnerId, d.Value1, 5, d.TargetId));
            if (d.Value2 != 0) into.Add(new DamageNumber(d.OwnerId, d.Value2, 6, d.TargetId));
            if (d.Value3 != 0) into.Add(new DamageNumber(d.OwnerId, d.Value3, 7, d.TargetId));
            return;
        }
        if (d.Mode == 3)
        {
            // OnDotDamage (scenestategame.cpp:5663) returns when the object hit (the second int) is none; the record then reaches
            // PCBase::OnDotDamageRecord (pcbase.cpp:3958): vfunc_111(total damage, first type, the attacker). The i64 damage is
            // kept whole. STILL-OPEN: IsOutsideSightRange and the magic service's delivery (FUN_14146bc20).
            if (isObject == null || !isObject(d.TargetId)) return;
            into.Add(new DamageNumber(d.TargetId, d.DotHit.Damage < 0 ? -d.DotHit.Damage : d.DotHit.Damage, d.DotHit.Flag, d.OwnerId));
            return;
        }
        if (d.Mode != 1 || !HasDamage(d, damage)) return;
        foreach (var t in d.Targets)
            foreach (var h in t.Hits)
            {
                int type = h.Flag == 8 ? 3 : h.Flag;
                into.Add(new DamageNumber(t.TargetId, h.Damage < 0 ? -h.Damage : h.Damage, type, d.OwnerId));
            }
    }

    /// <summary>
    /// The gate of a mode 1 packet's hits on every target (the skill, level, motion and attack are the packet's): CDamageRecord::
    /// Init (@0x14175a610, skillrecord.cpp:465) fails for a skill or level the table lacks, and the handler returns
    /// (scenestategame.cpp:5624-5626); PCBase::FUN_141671c20 (pcbase.cpp:3926-3929) does nothing for an attack the level
    /// lacks (GetAttackData, skillrecord.cpp:501) and gives the numbers and vfunc_112 only when its damage HasDamage
    /// (skillattackdata.cpp:384). False with no table.
    /// </summary>
    public static bool HasDamage(SkillDamage d, SkillDamageTable damage) =>
        damage?.Get(d.SkillId, d.Level, d.Motion, d.AttackPoint)?.HasDamage == true;

    /// <summary>
    /// UIDamageText::FUN_141913bd0 (@0x141913bd0, uidamagetext.cpp:609): grade 4 never shows. The masks: 1 the own
    /// player, 2 a party member's PC, 4 another PC, 0 anything else (<paramref name="pcMask"/> gives 2, 4 or 0 for an id
    /// that is not the own player). A caster that is the target takes the target's mask. Recover grades follow the three
    /// recover options; the rest shows when the take option has the target's bit or the give option the caster's
    /// (giveDamage m_n08, takeDamage m_n0c).
    /// STILL-OPEN: a pet caster stands for its owner, and an npc caster with vfunc_116 above 0 shows when it is the own
    /// player's or its npc data has m_bAttackDamage (no pets or summons here).
    /// </summary>
    public static bool Shows(GameOption.Battle o, int grade, int targetId, int casterId, int selfId, System.Func<int, int> pcMask)
    {
        if (grade == 4) return false;
        int targetMask = targetId == selfId ? 1 : pcMask(targetId);
        int casterMask = 0;
        if ((uint)casterId > 0)
            casterMask = casterId == targetId ? targetMask : casterId == selfId ? 1 : pcMask(casterId);
        if (IsRecover(grade))
        {
            if ((targetMask & 1) != 0) return o.DisplayMyTakeRecovery;
            if ((casterMask & 1) != 0) return o.DisplayMyGiveRecovery;
            if ((casterMask & 2) != 0 || (targetMask & 2) != 0) return o.DisplayPartyRecovery;
            return false;
        }
        return (o.TakeDamage & targetMask) != 0 || (o.GiveDamage & casterMask) != 0;
    }
}
