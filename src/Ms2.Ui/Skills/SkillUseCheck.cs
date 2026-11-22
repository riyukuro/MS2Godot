using System;
using Ms2.Net.Packets;

namespace Ms2.Ui.Skills;

/// <summary>What MyPC::FUN_141525580 reads of the own pc besides the record.</summary>
public sealed class UseCheckWorld
{
    /// <summary>GetCurStateId (gostate.h).</summary>
    public int StateId;
    /// <summary>MyPC::FUN_141527960 (mypc.cpp:1996): the hold state (0x20) whose FUN_1417cd060 is set.</summary>
    public bool HoldBusy;
    /// <summary>GetRidee is set.</summary>
    public bool Riding;
    /// <summary>PCBase::FUN_141675ed0 (pcbase.cpp:4328): the hit report's entity is water (kind 8); the ridee's when riding.</summary>
    public bool OnWater;
    /// <summary>AdditionalCliList::FUN_140970a00 (additionalcli_2.cpp:557): the buffs' m_motion.m_nNoFly.</summary>
    public bool NoFly;
    /// <summary>PC::FUN_14165f150 (pc.cpp:1926), m_n1050.</summary>
    public bool HeldFlag = true;
    /// <summary>GetHeldItem is set, and that item's skill (CItemData::FUN_14053dc90).</summary>
    public bool HasHeldItem;
    public int HeldItemSkill;
    /// <summary>CSkillSystem::IsEmotionLearned(id, level).</summary>
    public Func<uint, ushort, bool> EmotionLearned = (_, _) => false;
    /// <summary>AdditionalCliList::HasEnabledWithId (additionalcli_2.cpp:758).</summary>
    public Func<uint, bool> HasEnabledEffect = _ => false;
}

/// <summary>MyPC::FUN_141525580 @0x141525580 (mypc.cpp:2434), the first check of MyPC::vfunc_102 (:1150).</summary>
public static class SkillUseCheck
{
    /// <summary>GetCurStateId values that refuse every skill: gosGrabTarget, gosPlayInstrument (gostate.h).</summary>
    public const int StateGrabTarget = 0x2b, StatePlayInstrument = 0x2f;

    public enum Result
    {
        Ok,
        Refused,
        /// <summary>The skill rides off first (IsUnrideOnUse with a ridee): the request is sent, the use is refused.</summary>
        RideOff,
    }

    /// <summary>
    /// The checks in the client's order: states 0x2b / 0x2f; FUN_141527960; IsUnrideOnUse with a ridee, which refuses after making
    /// the ride-off (<paramref name="rideOff"/>: a plain RideOffAction(0) when on water, else RideOffActionUseGlideItem for an
    /// item kind skill with an item uid, else RideOffActionUseSkill, each with the record's m_bAD and m_nA4); a glide skill
    /// (action 0x1e) under no-fly; main type 4 without m_n1050, a held item or with the held item's skill not the record's;
    /// an emotion not learned at the level; on water a skill with disableWater; a required effect not enabled.
    /// STILL-OPEN: the motion's m_fB8 without a physics body (:2489; the own body always has one here), the content shutdown
    /// 0x36 of the main types 17-19 (:2494, notice 0x2f7), and the item uid's item checks (g_143ab2a90.FUN_140a661e0, the
    /// ShutdownSkillItem cooldown, :2499-2511) are not checked.
    /// </summary>
    public static Result Check(CastRecord r, UseCheckWorld w, out RideOffAction rideOff)
    {
        rideOff = null;
        if (w.StateId is StateGrabTarget or StatePlayInstrument) return Result.Refused;
        if (w.HoldBusy) return Result.Refused;
        var d = r.Skill;
        if (d.IsUnrideOnUse && w.Riding)
        {
            if (w.OnWater) rideOff = new RideOffAction(0, 0);
            else if (d.IsItemKind && r.ItemUid != 0) rideOff = new RideOffUseGlideItem(0, r.ItemUid, r.HasSequence, r.Key);
            else rideOff = new RideOffUseSkill(0, (int)d.Id, r.HasSequence, r.Key);
            return Result.RideOff;
        }
        if (d.ActionKind == 0x1e && w.NoFly) return Result.Refused;
        if (d.MainType == 4 && (!w.HeldFlag || !w.HasHeldItem || w.HeldItemSkill != (int)r.Skill.Id)) return Result.Refused;
        if (d.IsEmotion && !w.EmotionLearned(r.Level.Skill?.Id ?? d.Id, r.Level.Level)) return Result.Refused;
        if (w.OnWater && d.DisableWater) return Result.Refused;
        foreach (var e in d.Condition.RequireEffect)
            if (!w.HasEnabledEffect(e)) return Result.Refused;
        return Result.Ok;
    }
}
