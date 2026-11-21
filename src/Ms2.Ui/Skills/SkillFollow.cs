namespace Ms2.Ui.Skills;

/// <summary>
/// The own skill state's (StateSkill, 0x10) rules for a press while its cast runs, and for the move input under it.
/// StateSkill::vfunc_8 @0x1417e6990 (stateskill.cpp:633-641) polls the press each frame through FUN_1417eeb10 (:2996): the
/// pressed key's record (MyPC::vfunc_104) becomes the state's next cast only when <see cref="MayFollow"/> lets it, and
/// FUN_1417ef4a0 (:3091) starts it once the combo keys allow (<see cref="ChainsNow"/>); a press it refuses is dropped (the
/// pressed key is read once, WasKeyPressed in MyPC::FUN_141526730, mypc.cpp:1770). So the basic attack (a key-down skill
/// whose motion has a selfInputCombo key) pressed again takes the press only after its selfInputCombo key and starts it
/// again at "selfcombo" or the sequence's end, never earlier; the state's end (FUN_1417ee120, :2885) casts the key held or
/// pressed then.
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

        /// <summary>EndOfSequence @0x1417e9130 (stateskill.cpp:1062-1070) for the target sequence: m_b116, m_b49, m_b112.</summary>
        public void EndOfSequence() { SelfCombo = true; Combo = true; Ended = true; }
    }

    /// <summary>
    /// The running motion's combo text key times in its sequence (-1 for a key the sequence lacks): "inputcombo",
    /// "endInputCombo", the motion data's selfInputCombo key (SkillMotionData::Load, skillleveldata.cpp:1165),
    /// "endSelfInputcombo", "combo" and "selfcombo" (StateSkill::TextKeyEvent, stateskill.cpp:999-1027).
    /// </summary>
    public struct ComboKeys
    {
        public float InputCombo, EndInputCombo, SelfInput, EndSelfInput, Combo, SelfCombo;

        /// <summary>No keys.</summary>
        public static ComboKeys None => new() { InputCombo = -1, EndInputCombo = -1, SelfInput = -1, EndSelfInput = -1, Combo = -1, SelfCombo = -1 };

        static bool Crossed(float t, double prev, double pos) => t >= 0 && t > (float)prev && t <= (float)pos;   // in float, as the keys: a frame at the key time crosses it

        /// <summary>
        /// TextKeyEvent for the keys the sequence passed from <paramref name="prev"/> (exclusive) to <paramref name="pos"/>: the
        /// flags they set stay set for the state (the enter clears them, :406-410). Of inputcombo and endInputCombo passed in
        /// one frame the later one wins (the callbacks come in time order). <paramref name="canChainCombo"/>:
        /// StateSkill::CanChainCombo @0x1417f5060 (:1725) at the "combo" key. 0 B.
        /// </summary>
        public readonly void Advance(ref Window w, double prev, double pos, bool canChainCombo)
        {
            bool inC = Crossed(InputCombo, prev, pos), endC = Crossed(EndInputCombo, prev, pos);
            if (inC && endC) w.InputCombo = InputCombo > EndInputCombo;
            else if (inC) w.InputCombo = true;
            else if (endC) w.InputCombo = false;
            if (Crossed(SelfInput, prev, pos)) w.SelfInputCombo = true;
            if (Crossed(EndSelfInput, prev, pos)) w.EndSelfInput = true;
            if (Crossed(Combo, prev, pos) && canChainCombo) w.Combo = true;
            if (Crossed(SelfCombo, prev, pos)) w.SelfCombo = true;
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
    /// STILL-OPEN: the hold attack veto (m_b111, :3136-3142) and the auto change veto (m_b11e, :3150-3163) are not ported.
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

    /// <summary>CSkillRecord::IsLastMotion @0x141759f20 (skillrecord.cpp:337): m_nMotion + 1 &gt;= the level's motion count.</summary>
    public static bool IsLastMotion(CastRecord r) => r.Motion + 1 >= r.Level.Motions.Count;

    /// <summary>Unk_SkillBlockA4::IsOutputSkill @0x140802ff0 (skillleveldata.cpp:516): 0 without an input skill, else the output
    /// skill is <paramref name="id"/>.</summary>
    public static bool IsOutputSkill(SkillData s, uint id) => s.ComboInputSkill != 0 && s.ComboOutputSkill == id;

    /// <summary>
    /// The skill state takes the move input only for a skill whose moveType is 2 or 3 (StateSkill::vfunc_8 :642 and
    /// FUN_1417edc20 :2866: the input direction at the owner's speed); for any other skill the state moves by the motion
    /// alone, so a press while walking leaves the walk state (State::vfunc_8 @0x1417b7fb0, state.cpp:634-637, MyPC::vfunc_102's
    /// state change to 0x10) and the walk input is not read until the state ends (FUN_1417f0070 :2811: idle, 1).
    /// </summary>
    public static bool HoldsMove(SkillData s) => s != null && s.MoveType != 2 && s.MoveType != 3;
}
