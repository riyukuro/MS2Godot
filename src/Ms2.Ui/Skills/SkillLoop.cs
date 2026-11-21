namespace Ms2.Ui.Skills;

/// <summary>
/// StateSkill's continue loop (stateskill.cpp): a continue skill (&lt;kinds continue&gt;, m_bContinue) whose sequence has the
/// "loopstart" and "loopend" text keys plays its loop again while one of its keys is held, each round sent as a mode 3
/// (CSkillRecord::SendSkill) with its own cooldown.
/// </summary>
public sealed class SkillLoop
{
    /// <summary>m_bA5: the hold check may still run; set at the enter, cleared when a held skill cannot be cast.</summary>
    public bool Active;
    /// <summary>m_bCharging: a key of the skill is held inside the loop; the next loopend plays the loop again.</summary>
    public bool Charging;
    /// <summary>m_bLoop: sent with the mode 2 sync. Only the charging skill paths and EndOfSequence set it.
    /// STILL-OPEN: the charging skill's charging_attack / charging_combo paths (stateskill.cpp:698-719, :947-957).</summary>
    public bool Loop;
    /// <summary>m_nRepeat: the rounds left that play without a held key (<see cref="StartSequence"/>).</summary>
    public int Repeat;
    /// <summary>m_fChargeTime: 0 from the enter and each loopstart until the loopend; the hold check runs only while 0.</summary>
    public float ChargeTime;
    /// <summary>m_b117: inside the loop (loopstart seen, loopend not yet).</summary>
    public bool InLoop;
    /// <summary>m_fLoopStart / m_fLoopEnd: the text keys' times; m_fSpeed: the motion speed of the last round.</summary>
    public float LoopStart, LoopEnd, Speed = 1f;
    /// <summary>m_n138: the tick of the last loopstart; m_n134: the loop's length in ms; m_nChargeTick: how far into the loop
    /// the next round starts (the time the loopend came late, at most 50 ms).</summary>
    public uint LoopStartTick;
    public uint LoopMs, ChargeTick;

    public SkillLoop() => Reset();

    /// <summary>StateSkill::vfunc_4 (stateskill.cpp:395-420): the enter's loop fields.</summary>
    public void Reset()
    {
        Active = true;
        Charging = false;
        Loop = false;
        Repeat = 0;
        ChargeTime = 0f;
        InLoop = false;
        LoopStart = 0f;
        LoopEnd = 0f;
        Speed = 1f;
        LoopStartTick = 0;
        LoopMs = 0;
        ChargeTick = 0;
    }

    /// <summary>StateSkill::FUN_1417f0940 (stateskill.cpp:2226-2238), the motion's sequence start: m_nRepeat the motion's
    /// loopCount; another player's charging skill gets 0x14 more (<paramref name="otherChargingSkill"/>).</summary>
    public void StartSequence(int loopCount, bool otherChargingSkill = false) =>
        Repeat = loopCount + (otherChargingSkill ? 0x14 : 0);

    /// <summary>vfunc_8 :649: the hold check runs while m_bA5, for a continue skill, and while m_fChargeTime is 0.</summary>
    public bool HoldCheckRuns(SkillData s) => Active && s != null && s.Continue && SkillCooltimes.NearlyZero(ChargeTime);

    /// <summary>
    /// StateSkill::vfunc_8 @0x1417e6990 (stateskill.cpp:649-694), once <see cref="HoldCheckRuns"/>: m_bCharging off, then
    /// with a key held (<paramref name="held"/>: a key of the skill's origin id is down, CKeyTable::FUN_14142c6c0 then
    /// IsKeyDown) and the sequence's loop keys (<paramref name="hasLoopKeys"/>): a charging skill (m_b111) charges while
    /// repeats are left; another charges when it can be cast (<paramref name="castable"/>, FUN_141765a70), and one that
    /// cannot stops the hold check for the rest of the state (m_bA5 off).
    /// </summary>
    public void Hold(bool held, bool hasLoopKeys, bool chargingSkill, bool castable)
    {
        Charging = false;
        if (!held) return;
        if (chargingSkill)
        {
            if (hasLoopKeys) Charging = Repeat > 0;
        }
        else if (castable)
        {
            if (hasLoopKeys) Charging = true;
        }
        else Active = false;
    }

    /// <summary>
    /// TextKeyEvent "loopstart" (stateskill.cpp:844-868): inside the loop; outside m_bLoop the loop's start time, a zero
    /// m_fChargeTime (the hold check runs again) and the tick. STILL-OPEN: the record's loop effect (FUN_1417598e0) is not
    /// played and m_b112 has no reader here.
    /// </summary>
    public void OnLoopStart(float loopStartKey, uint now)
    {
        InLoop = true;
        if (Loop) return;
        LoopStart = loopStartKey;
        ChargeTime = 0f;
        LoopStartTick = now;
    }

    /// <summary>
    /// TextKeyEvent "loopend" (stateskill.cpp:869-905): m_fChargeTime the event's time, out of the loop; while charging or a
    /// repeat is left, the next round: a repeat used, m_bCharging off, the motion speed, the loop's length
    /// (FUN_14028d260: seconds times 1000, truncated) and the charge tick (<see cref="ClampedElapsedAfter16ms"/> of the time
    /// since the loopstart, at most the length less 5, an unsigned compare). True when a next round starts.
    /// </summary>
    public bool OnLoopEnd(float eventTime, float loopEndKey, float speed, uint now)
    {
        ChargeTime = eventTime;
        InLoop = false;
        if (!Charging && Repeat <= 0) return false;
        if (Repeat > 0) Repeat--;
        Charging = false;
        InLoop = true;
        Speed = speed;
        LoopEnd = loopEndKey;
        LoopMs = (uint)(long)((LoopEnd - LoopStart) / Speed * 1000.0f);
        ChargeTick = (uint)ClampedElapsedAfter16ms(now - LoopStartTick, LoopMs);
        if (LoopMs - 5u < ChargeTick) ChargeTick = LoopMs - 5u;
        return true;
    }

    /// <summary>EndOfSequence @0x1417e9130 (stateskill.cpp:1056-1060): the sequence's end does not end the state while
    /// charging or with a repeat left.</summary>
    public bool HoldsEnd => Charging || Repeat > 0;

    /// <summary>StateSkill::ClampedElapsedAfter16ms @0x1417f1a10 (stateskill.cpp:1611): the time past the first 16 ms, at most
    /// 50; 0 when <paramref name="now"/> is not after <paramref name="start"/>.</summary>
    public static int ClampedElapsedAfter16ms(uint now, uint start)
    {
        uint e = now > start ? now - start : 0;
        return (int)(e > 16 ? (e - 16 > 50 ? 50 : e - 16) : 0);
    }

    /// <summary>
    /// StateSkill::FUN_1417f16f0 @0x1417f16f0 (stateskill.cpp:1622): a continue round's cooldown, the loop's length at the
    /// state's speed less the charge tick, not below 0. 0 also for a skill that is not continue (the caller sets none then).
    /// </summary>
    public static float RoundCooldown(float loopStartKey, float loopEndKey, float speed, uint chargeTick)
    {
        float left = (loopEndKey - loopStartKey) / speed;
        float tick = chargeTick * 0.001f;
        return left - tick < 0f ? 0f : left - tick;
    }
}
