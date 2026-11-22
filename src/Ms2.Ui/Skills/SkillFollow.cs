using System;
using System.Collections.Generic;

namespace Ms2.Ui.Skills;

/// <summary>
/// The own skill state's (StateSkill, 0x10) rules for a press while its cast runs, and for the move input under it.
/// StateSkill::vfunc_8 @0x1417e6990 (stateskill.cpp:633-641) polls the press each frame through FUN_1417eeb10 (:2996): the
/// pressed key's record (MyPC::vfunc_104) becomes the state's next cast only when <see cref="MayFollow"/> lets it, and
/// FUN_1417ef4a0 (:3091) starts it once the combo keys allow (<see cref="ChainsNow"/>); a press it refuses is dropped (the
/// pressed key is read once, WasKeyPressed in MyPC::FUN_141526730, mypc.cpp:1770). So the basic attack (a key-down skill
/// whose motion has a selfInputCombo key) pressed again takes the press only after its selfInputCombo key and starts it
/// again at "selfcombo" or the sequence's end, never earlier; the state's end (FUN_1417ee120, :2885) casts the next cast or
/// the key held or pressed then (<see cref="CastsAtEnd"/>).
/// </summary>
public static class SkillFollow
{
    /// <summary>The running state's combo windows its sequence's text keys open (StateSkill::TextKeyEvent, stateskill.cpp:999-1021).</summary>
    public struct Window
    {
        /// <summary>m_b4a: between "inputcombo" and "endInputCombo" (:1009-1012).</summary>
        public bool InputCombo;
        /// <summary>m_b114: the motion's selfInputCombo key passed (:1015-1018).</summary>
        public bool SelfInputCombo;
        /// <summary>m_b115: "endSelfInputcombo" passed (:1020).</summary>
        public bool EndSelfInput;
        /// <summary>The running record's key (m_nA4) is down (IsKeyDown, :3043).</summary>
        public bool KeyHeld;
        /// <summary>m_fChargeTime is 0 (IsNearlyEqual, :3041).</summary>
        public bool ChargeTimeZero;
        /// <summary>m_b49: the "combo" key passed while CanChainCombo (:999-1006), or the sequence's end (EndOfSequence :1065).</summary>
        public bool Combo;
        /// <summary>m_b116: the "selfcombo" key passed (:1022-1027), or the sequence's end (:1064).</summary>
        public bool SelfCombo;
        /// <summary>m_b112: the sequence ended (EndOfSequence @0x1417e9130, :1066).</summary>
        public bool Ended;
        /// <summary>m_b11c: between "startAutoChangeSkill" and "endAutoChangeSkill" (:1029-1033).</summary>
        public bool AutoChangeOn;
        /// <summary>m_b11d: "endAutoChangeSkill" passed (:1034-1038); an auto change record clears it (FUN_1417f0570, :2195).</summary>
        public bool AutoChangeEnd;
        /// <summary>m_b11e: an auto change record was made in this state (FUN_1417f0570, :2197).</summary>
        public bool AutoChanged;

        /// <summary>EndOfSequence @0x1417e9130 (stateskill.cpp:1062-1070) for the target sequence: m_b116, m_b49, m_b112.</summary>
        public void EndOfSequence() { SelfCombo = true; Combo = true; Ended = true; }

        /// <summary>The same flags (no boxing, 0 B).</summary>
        public readonly bool Same(in Window o) =>
            InputCombo == o.InputCombo && SelfInputCombo == o.SelfInputCombo && EndSelfInput == o.EndSelfInput && KeyHeld == o.KeyHeld
            && ChargeTimeZero == o.ChargeTimeZero && Combo == o.Combo && SelfCombo == o.SelfCombo && Ended == o.Ended
            && AutoChangeOn == o.AutoChangeOn && AutoChangeEnd == o.AutoChangeEnd && AutoChanged == o.AutoChanged;
    }

    /// <summary>A combo or auto change text key of the skill state (StateSkill::TextKeyEvent, stateskill.cpp:999-1038).</summary>
    public enum ComboKey : byte { Combo, InputCombo, EndInputCombo, SelfInput, EndSelfInput, SelfCombo, StartAutoChange, EndAutoChange }

    /// <summary>
    /// The running motion's combo text keys in its sequence, in time order, every key of each name: "combo", "inputcombo",
    /// "endInputCombo", the motion data's selfInputCombo key (SkillMotionData::Load, skillleveldata.cpp:1165),
    /// "endSelfInputcombo", "selfcombo", "startAutoChangeSkill" and "endAutoChangeSkill" (StateSkill::TextKeyEvent,
    /// stateskill.cpp:999-1038). Filled once per cast
    /// (<see cref="Set"/>); the list is reused, so a refill and every per-frame call allocate 0 B once it has grown.
    /// </summary>
    public sealed class ComboKeys
    {
        readonly List<float> times = new();
        readonly List<ComboKey> kinds = new();

        public int Count => times.Count;

        /// <summary>
        /// TextKeyEvent's test of one key (stateskill.cpp:999-1038), in its order: "combo", "inputcombo" and "selfcombo" by
        /// exact compare (CString ==), "endInputCombo" and "endSelfInputcombo" by EqualsNoCase, the motion's
        /// <paramref name="selfInput"/> (null or empty = none) exactly, then "startAutoChangeSkill" and "endAutoChangeSkill"
        /// by EqualsNoCase; null for any other key.
        /// </summary>
        public static ComboKey? Classify(string text, string selfInput)
        {
            if (text == null) return null;
            if (text == "combo") return ComboKey.Combo;
            if (text == "inputcombo") return ComboKey.InputCombo;
            if (string.Equals(text, "endInputCombo", StringComparison.OrdinalIgnoreCase)) return ComboKey.EndInputCombo;
            if (!string.IsNullOrEmpty(selfInput) && text == selfInput) return ComboKey.SelfInput;
            if (string.Equals(text, "endSelfInputcombo", StringComparison.OrdinalIgnoreCase)) return ComboKey.EndSelfInput;
            if (text == "selfcombo") return ComboKey.SelfCombo;
            if (string.Equals(text, "startAutoChangeSkill", StringComparison.OrdinalIgnoreCase)) return ComboKey.StartAutoChange;
            if (string.Equals(text, "endAutoChangeSkill", StringComparison.OrdinalIgnoreCase)) return ComboKey.EndAutoChange;
            return null;
        }

        public void Clear() { times.Clear(); kinds.Clear(); }

        /// <summary>The combo keys of a sequence's text keys (<paramref name="textKeys"/> may be null = none).</summary>
        public void Set(IReadOnlyList<(double time, string text)> textKeys, string selfInput)
        {
            Clear();
            if (textKeys == null) return;
            for (int i = 0; i < textKeys.Count; i++)
                if (Classify(textKeys[i].text, selfInput) is { } k) Add((float)textKeys[i].time, k);
        }

        /// <summary>A key at <paramref name="t"/>, after the keys at the same time (the callbacks come in time order).</summary>
        public void Add(float t, ComboKey k)
        {
            int i = times.Count;
            while (i > 0 && times[i - 1] > t) i--;
            times.Insert(i, t);
            kinds.Insert(i, k);
        }

        static bool Crossed(float t, double prev, double pos) => t > (float)prev && t <= (float)pos;   // in float, as the keys: a frame at the key time crosses it

        /// <summary>
        /// TextKeyEvent for each key the sequence passed from <paramref name="prev"/> (exclusive) to <paramref name="pos"/>, in
        /// time order, so of two keys passed in one frame the later one wins: the flags they set stay set for the state (the
        /// enter clears them, :406-410). <paramref name="canChainCombo"/>: StateSkill::CanChainCombo @0x1417f5060 (:1725) at
        /// the "combo" key. True when a key that calls FUN_1417ef4a0 passed (an accepted "combo", :1006, or "selfcombo",
        /// :1027). 0 B.
        /// </summary>
        public bool Advance(ref Window w, double prev, double pos, bool canChainCombo)
        {
            bool chain = false;
            for (int i = 0; i < times.Count; i++)
            {
                if (!Crossed(times[i], prev, pos)) continue;
                switch (kinds[i])
                {
                    case ComboKey.Combo: if (canChainCombo) { w.Combo = true; chain = true; } break;
                    case ComboKey.InputCombo: w.InputCombo = true; break;
                    case ComboKey.EndInputCombo: w.InputCombo = false; break;
                    case ComboKey.SelfInput: w.SelfInputCombo = true; break;
                    case ComboKey.EndSelfInput: w.EndSelfInput = true; break;
                    case ComboKey.SelfCombo: w.SelfCombo = true; chain = true; break;
                    case ComboKey.StartAutoChange: w.AutoChangeOn = true; w.AutoChangeEnd = false; break;
                    case ComboKey.EndAutoChange: w.AutoChangeOn = false; w.AutoChangeEnd = true; break;
                }
            }
            return chain;
        }

        /// <summary>The earliest key time after <paramref name="prev"/> and not after <paramref name="pos"/>, or -1. 0 B.</summary>
        public double Next(double prev, double pos)
        {
            for (int i = 0; i < times.Count; i++)
                if (Crossed(times[i], prev, pos)) return times[i];
            return -1;
        }
    }

    /// <summary>
    /// StateSkill::FUN_1417eed70 @0x1417eed70 (stateskill.cpp:3024-3083): whether <paramref name="next"/> may follow the running
    /// record. Not the same skill again unless it is a key-down skill; then by the next skill's immediateActive
    /// (<paramref name="chainType"/>, m_nChainType: 1 any other skill, 2 another origin skill), else only in the last motion
    /// inside a combo window, not while a continue skill keeps its key held, not against the motion's self combo, and never
    /// into the running skill or its combo output after endSelfInputcombo. <paramref name="jobChange01"/>: feature 0xca.
    /// <paramref name="chainType"/> keeps its value when the first test refuses (the client's m_nChainType is not written).
    /// </summary>
    public static bool MayFollow(CastRecord running, SkillData next, in Window w, bool jobChange01, ref int chainType)
    {
        var cur = running.Skill;
        if (!next.KeyIsDown && next.Id == cur.Id) return false;
        chainType = next.ImmediateActive;
        if (chainType == 1) return next.Id != cur.Id;
        if (chainType == 2) return next.OriginId != cur.OriginId;
        if (!IsLastMotion(running)) return false;
        if (!w.InputCombo && !w.SelfInputCombo) return false;
        // :3038-3045: a continue skill (a charging one only while m_fChargeTime is 0) with its key held
        if (cur.Continue && (!cur.ChargingSkill || w.ChargeTimeZero) && w.KeyHeld) return false;
        var motion = running.Motion < running.Level.Motions.Count ? running.Level.Motions[running.Motion] : null;
        if (motion != null && !string.IsNullOrEmpty(motion.SelfInputCombo))
        {
            if (!w.SelfInputCombo)
            {
                if (next.Id == cur.Id) return false;
                if (jobChange01 && IsOutputSkill(cur, next.Id)) return false;
            }
            else if (!w.InputCombo && next.Id != cur.Id) return false;
        }
        if (w.EndSelfInput && (next.Id == cur.Id || IsOutputSkill(cur, next.Id))) return false;
        return true;
    }

    /// <summary>
    /// StateSkill::FUN_1417ef4a0 @0x1417ef4a0 (stateskill.cpp:3091-3180): whether the queued next cast (m_spNext) starts now.
    /// It runs at the "combo" and "selfcombo" keys, after an accepted press of a chain type, and every frame from
    /// FUN_1417ee120 (:2885-2891, vfunc_3 :622) until the sequence ends. A chain flag must be up (m_b49 / m_b116 / m_b112, or a
    /// chain type) and the running record in its last motion (IsEndedOrLastMotion, skillrecord.cpp:326); MyPC::vfunc_108
    /// (mypc.cpp:1036) is 1. For a motion with a selfInputCombo key, after "combo" but before "selfcombo" the same skill
    /// (with feature 0xca also its combo output) waits, and after "selfcombo" alone only the same skill (or with 0xca the
    /// output) goes (:3107-3135); endSelfInputcombo vetoes the same skill and the output (:3143-3149).
    /// The auto change veto (m_b11e, :3150-3163) needs the skill change and is <see cref="SkillCast.AutoChangeVetoes"/>.
    /// STILL-OPEN: the hold attack veto (m_b111, :3136-3142) is not ported.
    /// </summary>
    public static bool ChainsNow(CastRecord running, SkillData next, int chainType, in Window w, bool jobChange01)
    {
        if (!w.Combo && !w.SelfCombo && chainType == 0 && !w.Ended) return false;
        if (!IsLastMotion(running)) return false;
        var cur = running.Skill;
        var motion = running.Motion < running.Level.Motions.Count ? running.Level.Motions[running.Motion] : null;
        if (motion != null && !string.IsNullOrEmpty(motion.SelfInputCombo))
        {
            if ((w.Combo || w.Ended) && !w.SelfCombo)
            {
                if (next.Id == cur.Id || (jobChange01 && IsOutputSkill(cur, next.Id))) return false;
            }
            else if (!w.Combo && w.SelfCombo)
            {
                if (next.Id != cur.Id && (!jobChange01 || !IsOutputSkill(cur, next.Id))) return false;
            }
        }
        if (w.EndSelfInput && (next.Id == cur.Id || IsOutputSkill(cur, next.Id))) return false;
        return true;
    }

    /// <summary>
    /// FUN_1417ee120 @0x1417ee120 (stateskill.cpp:2911-2928) at the end of the last motion: the next cast (m_spNext, else the
    /// key polled then) goes through vfunc_102 without FUN_1417ef4a0's combo tests; only the hold attack flag
    /// (<paramref name="holdAttack"/>, m_b111) sends the running skill's combo output to the idle state (FUN_1417f0070).
    /// STILL-OPEN: m_b111 is not kept by the port (the callers pass false).
    /// </summary>
    public static bool CastsAtEnd(CastRecord running, SkillData next, bool holdAttack) =>
        !(holdAttack && IsOutputSkill(running.Skill, next.Id));

    /// <summary>
    /// StateSkill::FUN_1417f0570 @0x1417f0570 (stateskill.cpp:2161-2181), its first part: a running record whose skill's
    /// changeSkill autoChange (Unk_SkillBlockB0 m_eAutoChange) is 1 with "startAutoChangeSkill" up (m_b11c) or 2 after
    /// "endAutoChangeSkill" (m_b11d); <paramref name="kind"/> 0 takes either, else only that autoChange.
    /// </summary>
    public static bool AutoChangeWanted(CastRecord running, int kind, in Window w)
    {
        if (running?.Skill == null) return false;
        int auto = running.Skill.Change.AutoChange;
        if (kind != 0 && kind != auto) return false;
        return auto switch { 1 => w.AutoChangeOn, 2 => w.AutoChangeEnd, _ => false };
    }

    /// <summary>
    /// FUN_1417ee120 (stateskill.cpp:2905-2915) at the end with nothing else to cast: the auto change record
    /// (FUN_1417f0570(2)) goes unless it is the running skill again and not a key-down skill, or its immediateActive is 0.
    /// </summary>
    public static bool AutoCastsAtEnd(CastRecord running, CastRecord auto) =>
        auto != null && !(!auto.Skill.KeyIsDown && auto.Skill.Id == running.Skill.Id) && auto.Skill.ImmediateActive != 0;

    /// <summary>CSkillRecord::IsLastMotion @0x141759f20 (skillrecord.cpp:337): m_nMotion + 1 &gt;= the level's motion count.</summary>
    public static bool IsLastMotion(CastRecord r) => r.Motion + 1 >= r.Level.Motions.Count;

    /// <summary>Unk_SkillBlockA4::IsOutputSkill @0x140802ff0 (skillleveldata.cpp:516): 0 without an input skill, else the output
    /// skill is <paramref name="id"/>.</summary>
    public static bool IsOutputSkill(SkillData s, uint id) => s.ComboInputSkill != 0 && s.ComboOutputSkill == id;
}
